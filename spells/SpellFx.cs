using Godot;
using System.Collections.Generic;

// Small helpers most spells end up needing: see-through materials, flashes,
// stretching a mesh between two points, and finding what's in a sphere.
public static class SpellFx
{
    public const uint WorldLayer = 1u;
    public const uint CharacterLayer = 1u << 1;
    public const uint StructureLayer = 1u << 2;
    public const uint DamageableMask = CharacterLayer | StructureLayer;
    public const uint AnythingMask = WorldLayer | DamageableMask;

    public static StandardMaterial3D Glow(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = color,
    };

    // An expanding, fading sphere
    public static void Flash(Node from, Vector3 center, float radius, Color color)
    {
        StandardMaterial3D material = Glow(color);
        var flash = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        from.GetTree().CurrentScene.AddChild(flash);
        flash.GlobalPosition = center;
        flash.Scale = Vector3.One * 0.2f;

        Tween tween = flash.CreateTween().SetParallel();
        tween.TweenProperty(flash, "scale", Vector3.One * radius, 0.18);
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.25);
        tween.Chain().TweenCallback(Callable.From(flash.QueueFree));
    }

    // A thin line from a to b that fades out: bolts, zaps, tracers
    public static void Line(Node from, Vector3 a, Vector3 b, Color color, float life = 0.15f, float thickness = 0.05f)
    {
        StandardMaterial3D material = Glow(color);
        var line = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = thickness, BottomRadius = thickness, Height = 1f, RadialSegments = 6 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        from.GetTree().CurrentScene.AddChild(line);
        StretchBetween(line, a, b);

        Tween tween = line.CreateTween();
        tween.TweenProperty(material, "albedo_color:a", 0f, life);
        tween.TweenCallback(Callable.From(line.QueueFree));
    }

    // Stretches a unit-height, Y-up mesh (a CylinderMesh with Height 1) from a to b
    public static void StretchBetween(Node3D node, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float length = d.Length();
        if (length < 0.001f) return;

        Basis basis = StonePillarSpell.BasisFromUp(d / length) * Basis.FromScale(new Vector3(1f, length, 1f));
        node.GlobalTransform = new Transform3D(basis, (a + b) / 2f);
    }

    // Every character or structure touching a sphere, each listed once
    public static List<Node3D> BodiesInSphere(Node3D from, Vector3 center, float radius, uint mask = DamageableMask)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = mask,
        };

        var bodies = new List<Node3D>();
        var seen = new HashSet<Node3D>();
        foreach (var result in from.GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
        {
            var body = result["collider"].As<Node3D>();
            if (body != null && seen.Add(body)) bodies.Add(body);
        }
        return bodies;
    }
}
