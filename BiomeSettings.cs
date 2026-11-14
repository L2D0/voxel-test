using Godot;

[GlobalClass]
public partial class BiomeSettings : Resource
{
    [Export] public string BiomeName = "Plains";
    [Export] public Color DebugColor = new Color(0.45f, 0.6f, 0.35f);

    [ExportGroup("Height")]
    [Export] public float BaseHeight = 32f;
    [Export] public float HeightAmplitude = 8f;
    [Export] public float FeatureSize = 64f;
    [Export(PropertyHint.Range, "0,1")] public float Smoothness = 0.5f;

    [ExportGroup("Shape")]
    [Export(PropertyHint.Range, "0,1")] public float Terracing = 0f;
    [Export] public float TerraceHeight = 8f;
    [Export(PropertyHint.Range, "0.02,1")] public float TerraceWallWidth = 0.15f;
    [Export] public float Overhang = 0f;
    [Export] public float OverhangSize = 20f;
    [Export] public float VerticalStretch = 1f;
    [Export(PropertyHint.Range, "0,1")] public float Eccentricity = 0f;

    private FastNoiseLite _heightNoise;
    private FastNoiseLite _shapeNoise;

    public void Init(int seed)
    {
        _heightNoise = new FastNoiseLite
        {
            Seed = seed,
            Frequency = 1f / FeatureSize,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = Mathf.RoundToInt(Mathf.Lerp(6f, 1f, Smoothness)),
        };
        _shapeNoise = new FastNoiseLite
        {
            Seed = seed + 1,
            Frequency = 1f / OverhangSize,
            FractalOctaves = 2,
        };
    }

    public float Density(Vector3 p)
    {
        float h = BaseHeight + _heightNoise.GetNoise2D(p.X, p.Z) * HeightAmplitude;

        if (Terracing > 0f)
        {
            float t = h / TerraceHeight;
            float step = Mathf.Floor(t);
            float f = t - step;

            // Flat for most of the step, then a short steep rise at the end
            float rise = Mathf.Clamp((f - (1f - TerraceWallWidth)) / TerraceWallWidth, 0f, 1f);
            rise = Mathf.SmoothStep(0f, 1f, rise);

            float stepped = (step + rise) * TerraceHeight;
            h = Mathf.Lerp(h, stepped, Terracing);
        }

        float falloff = Mathf.Lerp(1f, 0.1f, Eccentricity);
        float density = (h - p.Y) * falloff;

        if (Overhang > 0f)
            density += _shapeNoise.GetNoise3D(p.X, p.Y / VerticalStretch, p.Z) * Overhang;

        return density;
    }
}