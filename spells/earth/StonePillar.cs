using Godot;

// A round stone column. The rising, standing and crumbling all live in Extrusion;
// this only decides what a ring looks like.
public partial class StonePillar : Extrusion
{
    public float Radius = 1f;
    private static readonly Color Stone = new(0.47f, 0.43f, 0.38f);
    protected override bool RandomYaw => true;
    protected override (Mesh mesh, Shape3D shape, Material material) MakeSegment(int index)
    {
        float r = Radius * ShapeRng.RandfRange(0.85f, 1.1f);

        var mesh = new CylinderMesh
        {
            TopRadius = r * ShapeRng.RandfRange(0.85f, 1f),
            BottomRadius = r,
            Height = SegmentHeight,
            RadialSegments = 7,
            Rings = 1,
        };
        var shape = new CylinderShape3D { Radius = r, Height = SegmentHeight };
        var material = new StandardMaterial3D { AlbedoColor = Stone.Darkened(ShapeRng.RandfRange(0f, 0.25f)) };
        return (mesh, shape, material);
    }
}