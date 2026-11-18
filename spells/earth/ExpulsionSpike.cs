using Godot;

// The spike Earthen expulsion punches out of the ground. Purely visual: the launch itself is an
// impulse, not physics, so the spike has no collision and crumbles almost as soon as it's up.
public partial class ExpulsionSpike : Extrusion
{
    public float Radius = 0.7f;

    private static readonly Color Stone = new(0.45f, 0.41f, 0.36f);

    protected override bool Solid => false;
    protected override bool RandomYaw => true;

    // The top segment is a blunt point; the rest widen slightly toward the base
    protected override (Mesh mesh, Shape3D shape, Material material) MakeSegment(int index)
    {
        float r = Radius * (1f + index * 0.1f) * ShapeRng.RandfRange(0.9f, 1.1f);
        var mesh = new CylinderMesh
        {
            TopRadius = index == 0 ? r * 0.15f : r * 0.9f,
            BottomRadius = r,
            Height = SegmentHeight,
            RadialSegments = 6,
            Rings = 1,
        };
        var material = new StandardMaterial3D { AlbedoColor = Stone.Darkened(ShapeRng.RandfRange(0f, 0.2f)) };
        return (mesh, null, material);
    }
}
