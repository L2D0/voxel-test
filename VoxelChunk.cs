using Godot;
using System.Collections.Generic;

public partial class VoxelChunk : Node3D
{
    public const int Size = 32;
    public const int Samples = Size + 1;

    [Export] public int Seed = 1337;
    [Export] public float SurfaceHeight = 12f;
    [Export] public float HeightAmplitude = 6f;
    [Export] public float OverhangStrength = 8f;
    [Export] public bool ShowDebugPoints = false;

    private readonly float[] _density = new float[Samples * Samples * Samples];
    private FastNoiseLite _heightNoise;
    private FastNoiseLite _shapeNoise;

    public override void _Ready()
    {
        SetupNoise();
        GenerateDensity();
        BuildMesh();
        if (ShowDebugPoints) BuildDebugView();
    }

    private static int Index(int x, int y, int z) => x + Samples * (y + Samples * z);
    private static int CellIndex(int x, int y, int z) => x + Size * (y + Size * z);
    private static int CellIndex(Vector3I c) => CellIndex(c.X, c.Y, c.Z);
    private static Vector3 Corner(int i) => new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);

    public float GetDensity(int x, int y, int z) => _density[Index(x, y, z)];

    private void SetupNoise()
    {
        _heightNoise = new FastNoiseLite { Seed = Seed, Frequency = 0.02f };
        _shapeNoise = new FastNoiseLite { Seed = Seed + 1, Frequency = 0.05f };
    }

    private void GenerateDensity()
    {
        Vector3 origin = GlobalPosition;

        for (int z = 0; z < Samples; z++)
            for (int y = 0; y < Samples; y++)
                for (int x = 0; x < Samples; x++)
                {
                    float wx = origin.X + x;
                    float wy = origin.Y + y;
                    float wz = origin.Z + z;

                    float height = SurfaceHeight + _heightNoise.GetNoise2D(wx, wz) * HeightAmplitude;
                    float density = height - wy;
                    density += _shapeNoise.GetNoise3D(wx, wy, wz) * OverhangStrength;

                    _density[Index(x, y, z)] = density;
                }
    }

    private void BuildMesh()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var cellVertex = new int[Size * Size * Size];
        var d = new float[8];

        // Pass 1: one vertex per cell the surface crosses
        for (int z = 0; z < Size; z++)
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
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

        // Pass 2: one quad per sample edge the surface crosses
        for (int z = 0; z < Samples; z++)
            for (int y = 0; y < Samples; y++)
                for (int x = 0; x < Samples; x++)
                {
                    var p = new Vector3I(x, y, z);
                    bool solid0 = GetDensity(x, y, z) > 0;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        int u = (axis + 1) % 3;
                        int v = (axis + 2) % 3;
                        if (p[axis] >= Size || p[u] < 1 || p[u] >= Size || p[v] < 1 || p[v] >= Size) continue;

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

        if (indices.Count == 0) return;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.6f, 0.35f) },
        });

        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        AddChild(body);
    }

    private static void AddQuad(List<int> indices, int a, int b, int c, int d)
    {
        indices.Add(a); indices.Add(b); indices.Add(c);
        indices.Add(a); indices.Add(c); indices.Add(d);
    }

    private void BuildDebugView()
    {
        int solidCount = 0;
        foreach (float d in _density)
            if (d > 0) solidCount++;

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = Vector3.One * 0.3f },
        };
        multimesh.InstanceCount = solidCount;

        int i = 0;
        for (int z = 0; z < Samples; z++)
            for (int y = 0; y < Samples; y++)
                for (int x = 0; x < Samples; x++)
                {
                    if (GetDensity(x, y, z) > 0)
                        multimesh.SetInstanceTransform(i++, new Transform3D(Basis.Identity, new Vector3(x, y, z)));
                }

        AddChild(new MultiMeshInstance3D { Multimesh = multimesh });
    }
}