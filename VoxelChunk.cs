using Godot;
using System.Collections.Generic;

public partial class VoxelChunk : Node3D
{
    public const int Size = 32;
    public const int Samples = Size + 2;   // local coords -1 .. Size
    public const int Cells = Size + 1;     // local cells -1 .. Size-1

    public VoxelWorld World;

    private readonly float[] _density = new float[Samples * Samples * Samples];
    private MeshInstance3D _meshInstance;
    private CollisionShape3D _collisionShape;

    public override void _Ready()
    {
        _meshInstance = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.6f, 0.35f) },
        };
        AddChild(_meshInstance);

        var body = new StaticBody3D();
        _collisionShape = new CollisionShape3D();
        body.AddChild(_collisionShape);
        AddChild(body);

        GenerateDensity();
        BuildMesh();
    }

    private static int Index(int x, int y, int z) => (x + 1) + Samples * ((y + 1) + Samples * (z + 1));
    private static int CellIndex(int x, int y, int z) => (x + 1) + Cells * ((y + 1) + Cells * (z + 1));
    private static int CellIndex(Vector3I c) => CellIndex(c.X, c.Y, c.Z);
    private static Vector3 Corner(int i) => new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);

    private float GetDensity(int x, int y, int z) => _density[Index(x, y, z)];

    private void GenerateDensity()
    {
        Vector3 origin = GlobalPosition;

        for (int z = -1; z <= Size; z++)
            for (int y = -1; y <= Size; y++)
                for (int x = -1; x <= Size; x++)
                    _density[Index(x, y, z)] = World.SampleDensity(origin + new Vector3(x, y, z));
    }

    // Forwarded to the world so edits reach every chunk they touch
    public void Carve(Vector3 worldPoint, float radius) => World.Carve(worldPoint, radius);
    public void Fill(Vector3 worldPoint, float radius) => World.Fill(worldPoint, radius);

    public void ApplySphere(Vector3 worldPoint, float radius, bool fill)
    {
        Vector3 center = worldPoint - GlobalPosition;
        int minX = Mathf.Max(-1, Mathf.FloorToInt(center.X - radius - 1));
        int minY = Mathf.Max(-1, Mathf.FloorToInt(center.Y - radius - 1));
        int minZ = Mathf.Max(-1, Mathf.FloorToInt(center.Z - radius - 1));
        int maxX = Mathf.Min(Size, Mathf.CeilToInt(center.X + radius + 1));
        int maxY = Mathf.Min(Size, Mathf.CeilToInt(center.Y + radius + 1));
        int maxZ = Mathf.Min(Size, Mathf.CeilToInt(center.Z + radius + 1));

        if (minX > maxX || minY > maxY || minZ > maxZ) return;   // sphere doesn't touch this chunk

        for (int z = minZ; z <= maxZ; z++)
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float dist = new Vector3(x, y, z).DistanceTo(center);
                    int i = Index(x, y, z);
                    if (fill) _density[i] = Mathf.Max(_density[i], radius - dist);
                    else _density[i] = Mathf.Min(_density[i], dist - radius);
                }

        BuildMesh();
    }

    private void BuildMesh()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var cellVertex = new int[Cells * Cells * Cells];
        var d = new float[8];

        // Pass 1: one vertex per cell the surface crosses (including the border cells)
        for (int z = -1; z < Size; z++)
            for (int y = -1; y < Size; y++)
                for (int x = -1; x < Size; x++)
                {
                    int solid = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        d[i] = GetDensity(x + (i & 1), y + ((i >> 1) & 1), z + ((i >> 2) & 1));
                        if (d[i] > 0) solid++;
                    }

                    int ci = CellIndex(x, y, z);
                    if (solid == 0 || solid == 8)
                    {
                        cellVertex[ci] = -1;
                        continue;
                    }

                    Vector3 sum = Vector3.Zero;
                    int crossings = 0;
                    for (int i = 0; i < 8; i++)
                        for (int bit = 1; bit <= 4; bit <<= 1)
                        {
                            if ((i & bit) != 0) continue;
                            int j = i | bit;
                            if ((d[i] > 0) == (d[j] > 0)) continue;
                            float t = d[i] / (d[i] - d[j]);
                            sum += Corner(i).Lerp(Corner(j), t);
                            crossings++;
                        }

                    cellVertex[ci] = vertices.Count;
                    vertices.Add(new Vector3(x, y, z) + sum / crossings);

                    Vector3 gradient = new Vector3(
                        (d[1] + d[3] + d[5] + d[7]) - (d[0] + d[2] + d[4] + d[6]),
                        (d[2] + d[3] + d[6] + d[7]) - (d[0] + d[1] + d[4] + d[5]),
                        (d[4] + d[5] + d[6] + d[7]) - (d[0] + d[1] + d[2] + d[3]));
                    normals.Add(-gradient.Normalized());
                }

        // Pass 2: quads for the edges this chunk owns (starting sample in 0..Size-1)
        for (int z = 0; z < Size; z++)
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector3I(x, y, z);
                    bool solid0 = GetDensity(x, y, z) > 0;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        int u = (axis + 1) % 3;
                        int v = (axis + 2) % 3;

                        Vector3I q = p;
                        q[axis] += 1;
                        if (solid0 == (GetDensity(q.X, q.Y, q.Z) > 0)) continue;

                        Vector3I c0 = p, c1 = p, c2 = p, c3 = p;
                        c0[u] -= 1; c0[v] -= 1;
                        c1[v] -= 1;
                        c3[u] -= 1;

                        int a = cellVertex[CellIndex(c0)];
                        int b = cellVertex[CellIndex(c1)];
                        int c = cellVertex[CellIndex(c2)];
                        int e = cellVertex[CellIndex(c3)];

                        if (solid0) AddQuad(indices, a, e, c, b);
                        else AddQuad(indices, a, b, c, e);
                    }
                }

        if (indices.Count == 0)
        {
            _meshInstance.Mesh = null;
            _collisionShape.Shape = null;
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        _meshInstance.Mesh = mesh;
        _collisionShape.Shape = mesh.CreateTrimeshShape();
    }

    private static void AddQuad(List<int> indices, int a, int b, int c, int d)
    {
        indices.Add(a); indices.Add(b); indices.Add(c);
        indices.Add(a); indices.Add(c); indices.Add(d);
    }
}