using Godot;

// A placeholder Crystal anchor: a post that snapping spells lock onto. Lives on the Structures layer.
public partial class TestAnchor : StaticBody3D, ISnapTarget
{
    public float Height = 1.6f;
    public float Lifetime = 30f;
    public Color Color = new(0.6f, 0.85f, 1f, 0.8f);

    private float _age;

    public Vector3 SnapPoint => GlobalPosition + Vector3.Up * Height;

    public override void _Ready()
    {
        CollisionLayer = SpellFx.StructureLayer;
        CollisionMask = 0;

        AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 0.25f, Height = Height },
            Position = Vector3.Up * Height / 2f,
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.25f, Height = Height, RadialSegments = 6 },
            MaterialOverride = SpellFx.Glow(Color),
            Position = Vector3.Up * Height / 2f,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        if (_age >= Lifetime) QueueFree();
    }
}
