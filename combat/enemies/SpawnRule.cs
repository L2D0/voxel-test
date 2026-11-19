using Godot;

public enum SpawnSurface { Ground, WaterSurface, Underwater, Air, Ceiling, CaveFloor }

// Where and how often an enemy appears naturally. Stub for now: nothing reads these until
// world gen has biomes, depth bands and a spawn system (pipeline 9.15).
[GlobalClass]
public partial class SpawnRule : Resource
{
    [Export] public Godot.Collections.Array<string> Biomes = new();   // BiomeSettings.BiomeName values
    [Export] public SpawnSurface Surface = SpawnSurface.Ground;
    [Export] public float MinDepth = -9999f;    // metres below the surface; negative = above it
    [Export] public float MaxDepth = 9999f;
    [Export] public float Weight = 1f;          // relative rarity among enemies that could spawn here
    [Export] public int GroupMin = 1;
    [Export] public int GroupMax = 1;
    [Export] public int AreaCap = 6;            // most of this enemy alive around one spot
    [Export] public bool DayOnly = false;
    [Export] public bool NightOnly = false;
}
