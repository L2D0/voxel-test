using Godot;

// A flat stone wall. Rising, standing and crumbling all come from Extrusion;
// this only decides what a slab looks like.
public partial class StoneBarrier : Extrusion
{
    public float Width = 3.2f;
    public float Thickness = 0.8f;

    private static readonly Color Stone = new(0.5f, 0.46f, 0.4f);

    // Width runs along the local X axis, thickness along Z (the direction the caster was facing)
    protected override (Mesh mesh, Shape3D shape, Material material) MakeSegment(int index)
    {
        float width = Width * ShapeRng.RandfRange(0.94f, 1.04f);
        float thickness = Thickness * ShapeRng.RandfRange(0.9f, 1.1f);
        if (index == 0) width *= 0.95f;   // a slightly narrower top course reads as stacked stone

        var size = new Vector3(width, SegmentHeight, thickness);
        var material = new StandardMaterial3D { AlbedoColor = Stone.Darkened(ShapeRng.RandfRange(0f, 0.2f)) };
        return (new BoxMesh { Size = size }, new BoxShape3D { Size = size }, material);
    }
}
