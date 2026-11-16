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

public partial class VoxelWorld : Node3D
{
    [Export] public int Seed = 1337;
    [Export] public Node3D Player;
    [Export] public int MinChunkY = 0;
    [Export] public int MaxChunkY = 2;
    [Export] public int ChunksAppliedPerFrame = 4;
    [Export] public Godot.Collections.Array<BiomeSettings> Biomes = new();
    [Export] public float BiomeCellSize = 64f;
    [Export] public float BiomeBlend = 24f;

    private class BuildResult
    {
        public VoxelChunk Chunk;
        public Vector3I Coord;
        public float[] Density;
        public MeshData Mesh;
        public bool FromSave;
        public bool Failed;
    }

    private BiomeSettings[] _biomes;
    private readonly Dictionary<Vector3I, VoxelChunk> _chunks = new();
    private readonly Dictionary<Vector3I, float[]> _savedEdits = new();
    private readonly ConcurrentQueue<BuildResult> _finished = new();
    private readonly List<Vector3I> _toLoad = new();

    private int _drawDistance = 4;
    private int _activeJobs;
    private readonly int _maxJobs = Math.Max(1, System.Environment.ProcessorCount - 1);
    private Vector3I _lastCenter = new Vector3I(int.MaxValue, 0, int.MaxValue);
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

        Vector3I center = ChunkColumnOf(Player.GlobalPosition);
        if (center != _lastCenter || _dirty)
        {
            _lastCenter = center;
            _dirty = false;
            UpdateChunkSet(center);
        }

        StartJobs();
        ApplyFinished();

        if (!_spawnReady) CheckSpawn(center);
    }

    // ---------- Streaming ----------

    private static Vector3I ChunkColumnOf(Vector3 p) => new Vector3I(
        Mathf.FloorToInt(p.X / VoxelChunk.Size), 0, Mathf.FloorToInt(p.Z / VoxelChunk.Size));

    private static int ColumnDistSq(Vector3I a, Vector3I b)
    {
        int dx = a.X - b.X, dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private void UpdateChunkSet(Vector3I center)
    {
        // Unload anything past the draw distance (+1 ring of slack)
        int unloadSq = (_drawDistance + 1) * (_drawDistance + 1);
        var toRemove = new List<Vector3I>();
        foreach (Vector3I coord in _chunks.Keys)
            if (ColumnDistSq(coord, center) > unloadSq) toRemove.Add(coord);
        foreach (Vector3I coord in toRemove)
            UnloadChunk(coord);

        // Queue everything inside the draw distance that isn't loaded, nearest first
        _toLoad.Clear();
        int r = _drawDistance;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dz * dz > r * r) continue;
                for (int y = MinChunkY; y <= MaxChunkY; y++)
                {
                    var coord = new Vector3I(center.X + dx, y, center.Z + dz);
                    if (!_chunks.ContainsKey(coord)) _toLoad.Add(coord);
                }
            }
        _toLoad.Sort((a, b) => ColumnDistSq(a, center).CompareTo(ColumnDistSq(b, center)));
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
        };
        AddChild(chunk);
        _chunks[coord] = chunk;

        Vector3 origin = chunk.Position;   // copied to a plain value for the thread
        _savedEdits.Remove(coord, out float[] saved);
        chunk.PendingSave = saved;

        _activeJobs++;
        Task.Run(() =>
        {
            try
            {
                float[] density = saved ?? VoxelChunk.GenerateDensity(this, origin);
                MeshData mesh = VoxelChunk.BuildMeshData(this, origin, density);
                _finished.Enqueue(new BuildResult
                {
                    Chunk = chunk,
                    Coord = coord,
                    Density = density,
                    Mesh = mesh,
                    FromSave = saved != null,
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

            r.Chunk.ApplyBuild(r.Density, r.Mesh, r.FromSave);
            applied++;
        }
    }

    private void UnloadChunk(Vector3I coord)
    {
        VoxelChunk chunk = _chunks[coord];
        _chunks.Remove(coord);

        if (chunk.Modified && chunk.DensityData != null)
            _savedEdits[coord] = chunk.DensityData;
        else if (!chunk.HasData && chunk.PendingSave != null)
            _savedEdits[coord] = chunk.PendingSave;   // unloaded before its saved data was applied

        chunk.QueueFree();
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
    private void ApplyEdit(Vector3 point, float radius, bool fill) => Edit(point, radius, fill);
    private void Edit(Vector3 worldPoint, float radius, bool fill)
    {
        foreach (VoxelChunk chunk in _chunks.Values)
            chunk.ApplySphere(worldPoint, radius, fill);
    }
}