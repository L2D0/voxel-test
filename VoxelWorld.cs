using Godot;
using System.Collections.Generic;

public partial class VoxelWorld : Node3D
{
    [Export] public int Seed = 1337;
    [Export] public float SurfaceHeight = 32f;
    [Export] public float HeightAmplitude = 10f;
    [Export] public float OverhangStrength = 8f;
    [Export] public Vector3I WorldSizeInChunks = new Vector3I(4, 2, 4);

    public FastNoiseLite HeightNoise { get; private set; }
    public FastNoiseLite ShapeNoise { get; private set; }

    private readonly Dictionary<Vector3I, VoxelChunk> _chunks = new();

    public override void _Ready()
    {
        HeightNoise = new FastNoiseLite { Seed = Seed, Frequency = 0.02f };
        ShapeNoise = new FastNoiseLite { Seed = Seed + 1, Frequency = 0.05f };

        for (int z = 0; z < WorldSizeInChunks.Z; z++)
            for (int y = 0; y < WorldSizeInChunks.Y; y++)
                for (int x = 0; x < WorldSizeInChunks.X; x++)
                {
                    var coord = new Vector3I(x, y, z);
                    var chunk = new VoxelChunk
                    {
                        World = this,
                        Name = $"Chunk_{x}_{y}_{z}",
                        Position = (Vector3)(coord * VoxelChunk.Size),
                    };
                    AddChild(chunk);
                    _chunks[coord] = chunk;
                }
    }

    public float SampleDensity(Vector3 p)
    {
        float height = SurfaceHeight + HeightNoise.GetNoise2D(p.X, p.Z) * HeightAmplitude;
        float density = height - p.Y;
        density += ShapeNoise.GetNoise3D(p.X, p.Y, p.Z) * OverhangStrength;
        return density;
    }

    public void Carve(Vector3 worldPoint, float radius) => Edit(worldPoint, radius, false);
    public void Fill(Vector3 worldPoint, float radius) => Edit(worldPoint, radius, true);

    private void Edit(Vector3 worldPoint, float radius, bool fill)
    {
        foreach (VoxelChunk chunk in _chunks.Values)
            chunk.ApplySphere(worldPoint, radius, fill);
    }
}