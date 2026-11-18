using Godot;

// A slab about three players wide, extruded out of any surface the way a pillar is.
// From the ground it's a wall, side-on to the caster: thick enough to stand on, too short to be
// a useful elevator. From a cliff face it sticks straight out as a ledge or step.
[GlobalClass]
public partial class RaiseBarrierSpell : SpellDefinition
{
    [ExportGroup("Barrier")]
    [Export] public float Width = 3.2f;
    [Export] public float Height = 2.4f;
    [Export] public float Thickness = 0.8f;
    [Export] public float RiseSpeed = 9f;
    [Export] public float Windup = 0.1f;
    [Export] public float Lifetime = 8f;
    [Export] public float WarningTime = 1.5f;
    [Export] public float SegmentHeight = 0.6f;
    [Export] public float MaxHealth = 250f;

    private static readonly Color ValidColor = new(0.55f, 0.9f, 0.55f, 0.3f);
    private static readonly Color InvalidColor = new(1f, 0.35f, 0.3f, 0.3f);

    public RaiseBarrierSpell()
    {
        SpellName = "Raise Barrier";
        Element = Element.Earth;
        Mode = CastMode.Instant;
        Range = 20f;
        RechargeTime = 6f;
        Cost = 25f;
    }

    public override bool IsTargetValid(SpellContext ctx) =>
        ctx.AimHit
        && ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= ctx.Stat(nameof(Range))
        && ctx.AimCollider is CollisionObject3D body
        && (body.CollisionLayer & SpellFx.WorldLayer) != 0;   // terrain or another extrusion

    // Local Y points out of the surface (the way it extrudes), X runs along the slab's width,
    // Z through its thickness. Built only from values sent with the cast, so every machine
    // turns it the same way.
    private static Basis Facing(SpellContext ctx)
    {
        Vector3 up = ctx.AimNormal;

        // Ground or ceiling: side-on to the caster. Walls and cliffs: width runs level, so it's a flat step.
        Vector3 reference = Mathf.Abs(up.Y) >= 0.7f
            ? new Vector3(ctx.AimDirection.X, 0f, ctx.AimDirection.Z)
            : Vector3.Up;

        Vector3 forward = reference - up * reference.Dot(up);   // flattened onto the surface
        if (forward.LengthSquared() < 0.0001f)
            forward = Mathf.Abs(up.Dot(Vector3.Forward)) < 0.9f ? Vector3.Forward : Vector3.Right;
        forward = (forward - up * forward.Dot(up)).Normalized();

        Vector3 right = up.Cross(forward);
        return new Basis(right, up, forward);
    }

    public override bool Begin(SpellContext ctx)
    {
        float height = ctx.Stat(nameof(Height));

        var wall = new StoneBarrier
        {
            Ctx = ctx,
            Name = $"Barrier_{ctx.Caster.Name}_{ctx.CastId}",   // the same on every machine
            Axis = ctx.AimNormal,   // straight out of the surface, like a pillar
            Foundation = Extrusion.FoundationAt(ctx.Caster, ctx.AimPoint, ctx.AimNormal),

            MaxHeight = height,
            MinHeight = height,
            RiseSpeed = ctx.Stat(nameof(RiseSpeed)),
            Windup = ctx.Stat(nameof(Windup)),
            Lifetime = ctx.Stat(nameof(Lifetime)),
            WarningTime = ctx.Stat(nameof(WarningTime)),
            SegmentHeight = ctx.Stat(nameof(SegmentHeight)),
            MaxHealth = ctx.Stat(nameof(MaxHealth)),
            Width = ctx.Stat(nameof(Width)),
            Thickness = ctx.Stat(nameof(Thickness)),

            Transform = new Transform3D(Facing(ctx), ctx.AimPoint - ctx.AimNormal * 0.3f),
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(wall);
        ctx.Track(wall);   // respects MaxActive
        return true;
    }

    // A unit box; UpdatePreview scales it to the wall's size
    public override Node3D CreatePreview() => new MeshInstance3D
    {
        Mesh = new BoxMesh { Size = Vector3.One },
        MaterialOverride = SpellFx.Glow(ValidColor),
    };

    public override void UpdatePreview(Node3D preview, SpellContext ctx, bool valid)
    {
        float width = ctx.Stat(nameof(Width));
        float height = ctx.Stat(nameof(Height));
        float thickness = ctx.Stat(nameof(Thickness));

        preview.Visible = ctx.AimHit;
        Basis basis = Facing(ctx) * Basis.FromScale(new Vector3(width, height, thickness));
        preview.GlobalTransform = new Transform3D(basis, ctx.AimPoint + ctx.AimNormal * (height / 2f));

        var material = (StandardMaterial3D)((MeshInstance3D)preview).MaterialOverride;
        material.AlbedoColor = valid ? ValidColor : InvalidColor;
    }
}