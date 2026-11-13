using Godot;

public partial class VoxelChunk : Node3D
{
    public const int Size = 32;
    public const int Samples = Size + 1;

    [Export] public int Seed = 1337;
    [Export] public float SurfaceHeight = 12f;
    [Export] public float HeightAmplitude = 6f;
    [Export] public float OverhangStrength = 8f;

    private readonly float[] _density = new float[Samples * Samples * Samples];
    private FastNoiseLite _heightNoise;
    private FastNoiseLite _shapeNoise;

    public override void _Ready()
    {
        SetupNoise();
        GenerateDensity();
        BuildDebugView();
    }

    private static int Index(int x, int y, int z) => x + Samples * (y + Samples * z);

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