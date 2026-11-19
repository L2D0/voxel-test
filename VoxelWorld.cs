using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

public struct BiomeSample
{
    public int Primary;
    public int Secondary;
    public float Blend;
}

// One terrain edit: a sphere carved out or filled in. The log of these is the whole difference
// between the generated world and the current one.
public readonly record struct TerrainEdit(Vector3 Point, float Radius, bool Fill);

public partial class VoxelWorld : Node3D
{
    [Export] public int Seed = 1337;
    [Export] public Node3D Player;
    [Export] public int MinChunkY = 0;
    [Export] public int MaxChunkY = 2;
    [Export] public int ChunksAppliedPerFrame = 4;
    [Export] public int SimDistance = 3;   // host: chunk radius kept loaded around other players, for enemies and physics
    [Export] public Godot.Collections.Array<BiomeSettings> Biomes = new();
    [Export] public float BiomeCellSize = 64f;
    [Export] public float BiomeBlend = 24f;

    private class BuildResult
    {
        public VoxelChunk Chunk;
        public Vector3I Coord;
        public float[] Density;
        public MeshData Mesh;
        public int EditsApplied;   // how many of this chunk's edits the build included
        public bool Failed;
    }

    private BiomeSettings[] _biomes;
    private readonly Dictionary<Vector3I, VoxelChunk> _chunks = new();
    // Every edit since the world began, in order, on every machine. Chunks are rebuilt from the
    // seed and then these replayed, so an unloaded chunk costs nothing to remember.
    private readonly List<TerrainEdit> _edits = new();
    private readonly Dictionary<Vector3I, List<TerrainEdit>> _editsByChunk = new();   // same edits, per chunk they touch
    private const int EditsPerMessage = 512;   // late join: the log goes over in batches this size
    private readonly ConcurrentQueue<BuildResult> _finished = new();
    private readonly List<Vector3I> _toLoad = new();

    private int _drawDistance = 4;
    private int _activeJobs;
    private readonly int _maxJobs = Math.Max(1, System.Environment.ProcessorCount - 1);
    // Where terrain is wanted: the local player at draw distance first, then (host only) every other
    // player at SimDistance, so enemies and anything else the host simulates have ground under them.
    private readonly List<(Vector3I Center, int Radius)> _areas = new();
    private readonly List<(Vector3I Center, int Radius)> _lastAreas = new();
    private bool _dirty = true;
    private bool _spawnReady;
    private bool _initialized;

    // ---------- Setup ----------

    public override void _Ready()
    {
        AddToGroup("voxel_world");

        _drawDistance = Settings.DrawDistance;
        Settings.DrawDistanceChanged += OnDrawDistanceChanged;
    }

    public override void _ExitTree()
    {
        // Static events keep subscribers alive: always unsubscribe on the way out
        Settings.DrawDistanceChanged -= OnDrawDistanceChanged;
    }

    // Called once the session knows the world seed (host: immediately, client: when received)
    public void Initialize(int seed)
    {
        if (Biomes.Count == 0)
        {
            GD.PushError("VoxelWorld has no biomes assigned");
            SetProcess(false);
            return;
        }

        Seed = seed;

        // Copy to a plain C# array so worker threads never touch a Godot collection
        _biomes = new BiomeSettings[Biomes.Count];
        for (int i = 0; i < Biomes.Count; i++)
        {
            _biomes[i] = Biomes[i];
            _biomes[i].Init(Seed);
        }

        _initialized = true;
        _dirty = true;

        if (Multiplayer.IsServer()) Multiplayer.PeerConnected += SendEditLog;
    }

    // The player this machine streams terrain around
    public void SetPlayer(Node3D player)
    {
        Player = player;
        Player.ProcessMode = ProcessModeEnum.Disabled;   // frozen until the ground exists
        _spawnReady = false;
        _dirty = true;
    }

    private void OnDrawDistanceChanged(int value)
    {
        _drawDistance = value;
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (!_initialized || Player == null) return;

        GatherAreas();
        if (_dirty || !SameAsLast())
        {
            _lastAreas.Clear();
            _lastAreas.AddRange(_areas);
            _dirty = false;
            UpdateChunkSet();
        }

        StartJobs();
        ApplyFinished();

        if (!_spawnReady) CheckSpawn(_areas[0].Center);
    }

    // ---------- Streaming ----------

    private static Vector3I ChunkColumnOf(Vector3 p) => new Vector3I(
        Mathf.FloorToInt(p.X / VoxelChunk.Size), 0, Mathf.FloorToInt(p.Z / VoxelChunk.Size));

    private static int ColumnDistSq(Vector3I a, Vector3I b)
    {
        int dx = a.X - b.X, dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private void GatherAreas()
    {
        _areas.Clear();
        _areas.Add((ChunkColumnOf(Player.GlobalPosition), _drawDistance));

        if (!Multiplayer.IsServer()) return;   // clients only ever need their own surroundings
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Node3D other && other != Player)
                _areas.Add((ChunkColumnOf(other.GlobalPosition), SimDistance));
    }

    private bool SameAsLast()
    {
        if (_areas.Count != _lastAreas.Count) return false;
        for (int i = 0; i < _areas.Count; i++)
            if (_areas[i] != _lastAreas[i]) return false;
        return true;
    }

    // Inside any area, with this many rings of slack
    private bool Wanted(Vector3I coord, int slack)
    {
        foreach ((Vector3I center, int radius) in _areas)
        {
            int r = radius + slack;
            if (ColumnDistSq(coord, center) <= r * r) return true;
        }
        return false;
    }

    // Only what the local player can see gets drawn; chunks kept for other players just collide
    private bool Shown(Vector3I coord)
    {
        int r = _drawDistance + 1;
        return ColumnDistSq(coord, _areas[0].Center) <= r * r;
    }

    // Nearest first, and the local player's surroundings ahead of everyone else's
    private int Priority(Vector3I coord)
    {
        int best = ColumnDistSq(coord, _areas[0].Center);
        for (int i = 1; i < _areas.Count; i++)
            best = Math.Min(best, ColumnDistSq(coord, _areas[i].Center) + 4);
        return best;
    }

    private void UpdateChunkSet()
    {
        // Unload anything outside every area (+1 ring of slack); show or hide the rest
        var toRemove = new List<Vector3I>();
        foreach (var (coord, chunk) in _chunks)
        {
            if (!Wanted(coord, 1)) toRemove.Add(coord);
            else chunk.Visible = Shown(coord);
        }
        foreach (Vector3I coord in toRemove)
            UnloadChunk(coord);

        // Queue everything inside an area that isn't loaded
        _toLoad.Clear();
        var queued = new HashSet<Vector3I>();
        foreach ((Vector3I center, int r) in _areas)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dz * dz > r * r) continue;
                    for (int y = MinChunkY; y <= MaxChunkY; y++)
                    {
                        var coord = new Vector3I(center.X + dx, y, center.Z + dz);
                        if (!_chunks.ContainsKey(coord) && queued.Add(coord)) _toLoad.Add(coord);
                    }
                }
        _toLoad.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
    }

    private void StartJobs()
    {
        while (_activeJobs < _maxJobs && _toLoad.Count > 0)
        {
            Vector3I coord = _toLoad[0];
            _toLoad.RemoveAt(0);
            if (!_chunks.ContainsKey(coord)) LoadChunk(coord);
        }
    }

    private void LoadChunk(Vector3I coord)
    {
        var chunk = new VoxelChunk
        {
            World = this,
            Coord = coord,
            Name = $"Chunk_{coord.X}_{coord.Y}_{coord.Z}",
            Position = (Vector3)(coord * VoxelChunk.Size),
            Visible = Shown(coord),
        };
        AddChild(chunk);
        _chunks[coord] = chunk;

        Vector3 origin = chunk.Position;   // copied to a plain value for the thread
        TerrainEdit[] edits = EditsFor(coord).ToArray();   // a snapshot: the thread never sees the live list

        _activeJobs++;
        Task.Run(() =>
        {
            try
            {
                float[] density = VoxelChunk.GenerateDensity(this, origin);
                foreach (TerrainEdit e in edits)
                    VoxelChunk.ApplySphereTo(density, e.Point - origin, e.Radius, e.Fill);

                MeshData mesh = VoxelChunk.BuildMeshData(this, origin, density);
                _finished.Enqueue(new BuildResult
                {
                    Chunk = chunk,
                    Coord = coord,
                    Density = density,
                    Mesh = mesh,
                    EditsApplied = edits.Length,
                });
            }
            catch (Exception e)
            {
                GD.PushError($"Chunk {coord} failed: {e}");
                _finished.Enqueue(new BuildResult { Chunk = chunk, Coord = coord, Failed = true });
            }
        });
    }

    private void ApplyFinished()
    {
        int applied = 0;
        while (applied < ChunksAppliedPerFrame && _finished.TryDequeue(out BuildResult r))
        {
            _activeJobs--;
            if (r.Failed) continue;

            // The chunk may have been unloaded while its job was running
            if (!_chunks.TryGetValue(r.Coord, out VoxelChunk current) || current != r.Chunk) continue;

            r.Chunk.ApplyBuild(r.Density, r.Mesh);

            // Edits that arrived while the build was running
            List<TerrainEdit> edits = EditsFor(r.Coord);
            for (int i = r.EditsApplied; i < edits.Count; i++)
                r.Chunk.ApplySphere(edits[i].Point, edits[i].Radius, edits[i].Fill);
            applied++;
        }
    }

    private void UnloadChunk(Vector3I coord)
    {
        VoxelChunk chunk = _chunks[coord];
        _chunks.Remove(coord);
        chunk.QueueFree();   // nothing to keep: the edit log rebuilds it next time
    }

    private void CheckSpawn(Vector3I center)
    {
        for (int y = MinChunkY; y <= MaxChunkY; y++)
        {
            var coord = new Vector3I(center.X, y, center.Z);
            if (!_chunks.TryGetValue(coord, out VoxelChunk c) || !c.HasData) return;
        }
        _spawnReady = true;
        Player.ProcessMode = ProcessModeEnum.Inherit;
    }

    // ---------- Biome regions (Voronoi) ----------

    public BiomeSample SampleBiomes(float x, float z)
    {
        int cx = Mathf.FloorToInt(x / BiomeCellSize);
        int cz = Mathf.FloorToInt(z / BiomeCellSize);
        var pos = new Vector2(x, z);

        float best = float.MaxValue, second = float.MaxValue;
        int bestBiome = 0, secondBiome = 0;

        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                float d = CellPoint(gx, gz).DistanceTo(pos);
                int biome = CellBiome(gx, gz);

                if (d < best)
                {
                    second = best; secondBiome = bestBiome;
                    best = d; bestBiome = biome;
                }
                else if (d < second)
                {
                    second = d; secondBiome = biome;
                }
            }

        float edge = Mathf.Clamp((second - best) / BiomeBlend, 0f, 1f);
        return new BiomeSample
        {
            Primary = bestBiome,
            Secondary = secondBiome,
            Blend = 0.5f * (1f - Mathf.SmoothStep(0f, 1f, edge)),
        };
    }

    private Vector2 CellPoint(int gx, int gz) => new Vector2(
        (gx + Hash01(gx, gz, 11)) * BiomeCellSize,
        (gz + Hash01(gx, gz, 23)) * BiomeCellSize);

    private int CellBiome(int gx, int gz) => (int)(Hash(gx, gz, Seed + 37) % (uint)_biomes.Length);

    private static uint Hash(int x, int z, int seed)
    {
        uint h = (uint)seed;
        h ^= (uint)x * 0x27d4eb2dU;
        h = (h << 13) | (h >> 19);
        h ^= (uint)z * 0x165667b1U;
        h *= 0x85ebca6bU;
        h ^= h >> 13;
        h *= 0xc2b2ae35U;
        h ^= h >> 16;
        return h;
    }

    private float Hash01(int x, int z, int salt) => (Hash(x, z, Seed + salt) & 0xFFFFFF) / (float)0x1000000;

    // ---------- Density and colour (called from worker threads) ----------

    public float Density(Vector3 p, BiomeSample s)
    {
        float d = _biomes[s.Primary].Density(p);
        if (s.Blend > 0.001f && s.Secondary != s.Primary)
            d = Mathf.Lerp(d, _biomes[s.Secondary].Density(p), s.Blend);
        return d;
    }

    public Color BiomeColor(Vector3 p)
    {
        BiomeSample s = SampleBiomes(p.X, p.Z);
        return _biomes[s.Primary].DebugColor.Lerp(_biomes[s.Secondary].DebugColor, s.Blend);
    }

    // ---------- Editing ----------

    public void Carve(Vector3 worldPoint, float radius) => SubmitEdit(worldPoint, radius, false);
    public void Fill(Vector3 worldPoint, float radius) => SubmitEdit(worldPoint, radius, true);

    // Edits go through the host, which broadcasts them to everyone, itself included
    private void SubmitEdit(Vector3 point, float radius, bool fill)
    {
        if (Multiplayer.IsServer()) Rpc(MethodName.ApplyEdit, point, radius, fill);
        else RpcId(1, MethodName.RequestEdit, point, radius, fill);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestEdit(Vector3 point, float radius, bool fill)
    {
        if (Multiplayer.IsServer()) Rpc(MethodName.ApplyEdit, point, radius, fill);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyEdit(Vector3 point, float radius, bool fill) => Edit(new TerrainEdit(point, radius, fill));

    // Every machine: log it, then change whatever's loaded. Chunks that load later replay it.
    private void Edit(TerrainEdit edit)
    {
        _edits.Add(edit);

        // Which chunks it touches, counting the one-sample border each chunk shares with its neighbours
        float reach = edit.Radius + 2f;
        Vector3I min = ChunkOf(edit.Point - Vector3.One * reach);
        Vector3I max = ChunkOf(edit.Point + Vector3.One * reach);
        for (int z = min.Z; z <= max.Z; z++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var coord = new Vector3I(x, y, z);
                    if (!_editsByChunk.TryGetValue(coord, out List<TerrainEdit> list))
                        _editsByChunk[coord] = list = new List<TerrainEdit>();
                    list.Add(edit);

                    if (_chunks.TryGetValue(coord, out VoxelChunk chunk))
                        chunk.ApplySphere(edit.Point, edit.Radius, edit.Fill);
                }
    }

    private static readonly List<TerrainEdit> NoEdits = new();
    private List<TerrainEdit> EditsFor(Vector3I coord) =>
        _editsByChunk.TryGetValue(coord, out List<TerrainEdit> list) ? list : NoEdits;

    private static Vector3I ChunkOf(Vector3 p) => new(
        Mathf.FloorToInt(p.X / VoxelChunk.Size),
        Mathf.FloorToInt(p.Y / VoxelChunk.Size),
        Mathf.FloorToInt(p.Z / VoxelChunk.Size));

    // Read-only view for saving (pipeline 4.1)
    public IReadOnlyList<TerrainEdit> EditLog => _edits;

    // ---------- Late join ----------

    // Host: a new player gets every edit so far, in batches, before any live ones
    private void SendEditLog(long peer)
    {
        for (int start = 0; start < _edits.Count; start += EditsPerMessage)
        {
            int count = Math.Min(EditsPerMessage, _edits.Count - start);
            var points = new Vector3[count];
            var radii = new float[count];
            var fills = new byte[count];
            for (int i = 0; i < count; i++)
            {
                TerrainEdit e = _edits[start + i];
                points[i] = e.Point;
                radii[i] = e.Radius;
                fills[i] = e.Fill ? (byte)1 : (byte)0;
            }
            RpcId(peer, MethodName.ReceiveEditBatch, points, radii, fills);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEditBatch(Vector3[] points, float[] radii, byte[] fills)
    {
        for (int i = 0; i < points.Length; i++)
            Edit(new TerrainEdit(points[i], radii[i], fills[i] != 0));
    }
}