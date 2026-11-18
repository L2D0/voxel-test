using Godot;

[GlobalClass]
public partial class StonePillarSpell : SpellDefinition
{
    [ExportGroup("Pillar")]
    [Export] public float MaxHeight = 6f;
    [Export] public float MinHeight = 1f;
    [Export] public float RiseSpeed = 2.5f;
    [Export] public float Windup = 0.25f;
    [Export] public float Lifetime = 5f;
    [Export] public float WarningTime = 1.5f;
    [Export] public float PillarRadius = 1f;
    [Export] public float RingHeight = 0.75f;
    [Export] public float MaxHealth = 100f;

    private static readonly Color ValidColor = new(0.55f, 0.9f, 0.55f, 0.3f);
    private static readonly Color InvalidColor = new(1f, 0.35f, 0.3f, 0.3f);

    public StonePillarSpell()
    {
        SpellName = "Stone Pillar";
        Element = Element.Earth;
        Mode = CastMode.Hold;
        Range = 20f;
    }

    public override bool IsTargetValid(SpellContext ctx) =>
        ctx.AimHit
        && ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= ctx.Stat(nameof(Range))
        && ctx.AimCollider is CollisionObject3D body
        && (body.CollisionLayer & SpellFx.WorldLayer) != 0;   // terrain or another extrusion

    public override bool Begin(SpellContext ctx)
    {
        var pillar = new StonePillar
        {
            Ctx = ctx,
            Name = $"Pillar_{ctx.Caster.Name}_{ctx.CastId}",   // the same on every machine
            Axis = ctx.AimNormal,
            Foundation = Extrusion.FoundationAt(ctx.Caster, ctx.AimPoint, ctx.AimNormal),

            MaxHeight = ctx.Stat(nameof(MaxHeight)),
            MinHeight = ctx.Stat(nameof(MinHeight)),
            RiseSpeed = ctx.Stat(nameof(RiseSpeed)),
            Windup = ctx.Stat(nameof(Windup)),
            Lifetime = ctx.Stat(nameof(Lifetime)),
            WarningTime = ctx.Stat(nameof(WarningTime)),
            SegmentHeight = ctx.Stat(nameof(RingHeight)),
            MaxHealth = ctx.Stat(nameof(MaxHealth)),
            Radius = ctx.Stat(nameof(PillarRadius)),

            // Starts just under the surface and rises out of it
            Transform = new Transform3D(BasisFromUp(ctx.AimNormal), ctx.AimPoint - ctx.AimNormal * 0.3f),
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(pillar);
        ctx.Instance = pillar;
        ctx.Track(pillar);   // respects MaxActive, if a node ever caps pillars
        return true;
    }

    // HeldTime here is the owner's, sent with the release, so every copy stops at the same height
    public override void Release(SpellContext ctx) => (ctx.Instance as StonePillar)?.Release(ctx.HeldTime);
    public override void Cancel(SpellContext ctx) => (ctx.Instance as StonePillar)?.Release(ctx.HeldTime);

    // A unit cylinder; UpdatePreview scales it to the effective height and radius
    public override Node3D CreatePreview() => new MeshInstance3D
    {
        Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 12 },
        MaterialOverride = SpellFx.Glow(ValidColor),
    };

    public override void UpdatePreview(Node3D preview, SpellContext ctx, bool valid)
    {
        float height = ctx.Stat(nameof(MaxHeight));
        float radius = ctx.Stat(nameof(PillarRadius));

        preview.Visible = ctx.AimHit;
        Basis basis = BasisFromUp(ctx.AimNormal) * Basis.FromScale(new Vector3(radius, height, radius));
        preview.GlobalTransform = new Transform3D(basis, ctx.AimPoint + ctx.AimNormal * (height / 2f));

        var material = (StandardMaterial3D)((MeshInstance3D)preview).MaterialOverride;
        material.AlbedoColor = valid ? ValidColor : InvalidColor;
    }

    public static Basis BasisFromUp(Vector3 up)
    {
        float d = up.Dot(Vector3.Up);
        if (d > 0.999f) return Basis.Identity;
        if (d < -0.999f) return new Basis(Vector3.Right, Mathf.Pi);
        return new Basis(new Quaternion(Vector3.Up, up));
    }
}
