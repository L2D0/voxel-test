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

    private static readonly Color ValidColor = new Color(0.55f, 0.9f, 0.55f, 0.3f);
    private static readonly Color InvalidColor = new Color(1f, 0.35f, 0.3f, 0.3f);

    public StonePillarSpell()
    {
        Mode = CastMode.Hold;
        Range = 20f;
    }

    public override bool IsTargetValid(SpellContext ctx) =>
        ctx.AimHit
        && ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= Range
        && ctx.AimCollider is CollisionObject3D body
        && (body.CollisionLayer & 1u) != 0;   // must be World (terrain or another pillar)

    public override bool Begin(SpellContext ctx)
    {
        if (!IsTargetValid(ctx)) return false;

        var pillar = new StonePillar
        {
            Spell = this,
            Axis = ctx.AimNormal,
            Transform = new Transform3D(BasisFromUp(ctx.AimNormal), ctx.AimPoint - ctx.AimNormal * 0.3f),
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(pillar);
        ctx.Instance = pillar;
        return true;
    }

    public override void Release(SpellContext ctx) => (ctx.Instance as StonePillar)?.StopRising();
    public override void Cancel(SpellContext ctx) => (ctx.Instance as StonePillar)?.StopRising();

    public override Node3D CreatePreview() => new MeshInstance3D
    {
        Mesh = new CylinderMesh { TopRadius = PillarRadius, BottomRadius = PillarRadius, Height = MaxHeight, RadialSegments = 12 },
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = ValidColor,
        },
    };

    public override void UpdatePreview(Node3D preview, SpellContext ctx, bool valid)
    {
        preview.Visible = ctx.AimHit;
        preview.GlobalTransform = new Transform3D(BasisFromUp(ctx.AimNormal), ctx.AimPoint + ctx.AimNormal * (MaxHeight / 2f));
        var material = (StandardMaterial3D)((MeshInstance3D)preview).MaterialOverride;
        material.AlbedoColor = valid ? ValidColor : InvalidColor;
    }

    // A rotation whose local up points along the given direction
    public static Basis BasisFromUp(Vector3 up)
    {
        float d = up.Dot(Vector3.Up);
        if (d > 0.999f) return Basis.Identity;
        if (d < -0.999f) return new Basis(Vector3.Right, Mathf.Pi);   // straight down (ceilings)
        return new Basis(new Quaternion(Vector3.Up, up));
    }
}