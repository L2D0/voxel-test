using Godot;

// MultiPoint test: click one spot, then another, and a damaging line joins them for a few seconds.
[GlobalClass]
public partial class TetherTestSpell : SpellDefinition
{
    [ExportGroup("Tether")]
    [Export] public float MaxLength = 15f;
    [Export] public float Lifetime = 5f;
    [Export] public float DamagePerTick = 8f;
    [Export] public float TickInterval = 0.5f;
    [Export] public float HeightAboveGround = 1f;
    [Export] public Color LineColor = new(0.6f, 0.85f, 1f, 0.7f);

    private static readonly Color InvalidColor = new(1f, 0.35f, 0.3f, 0.5f);

    public TetherTestSpell()
    {
        SpellName = "Test Tether";
        Element = Element.Crystal;
        Mode = CastMode.MultiPoint;
        PointCount = 2;
        Range = 30f;
        RechargeTime = 4f;
        AltMode = AltMode.CancelPicks;   // right click throws away the first pick
        SnapsToAnchors = true;           // picks lock onto test anchors near the crosshair
    }

    public override bool IsTargetValid(SpellContext ctx)
    {
        if (!ctx.AimHit || ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) > ctx.Stat(nameof(Range))) return false;
        if (ctx.Points.Length == 0) return true;   // first pick
        return ctx.Points[^1].DistanceTo(ctx.AimPoint) <= ctx.Stat(nameof(MaxLength));
    }

    public override bool Begin(SpellContext ctx)
    {
        if (ctx.Points.Length < 2) return false;

        Vector3 lift = Vector3.Up * ctx.Stat(nameof(HeightAboveGround));
        var line = new TetherLine
        {
            Ctx = ctx,
            A = ctx.Points[0] + lift,
            B = ctx.Points[1] + lift,
            Lifetime = ctx.Stat(nameof(Lifetime)),
            DamagePerTick = ctx.Stat(nameof(DamagePerTick)),
            TickInterval = ctx.Stat(nameof(TickInterval)),
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1f, RadialSegments = 8 },
            MaterialOverride = SpellFx.Glow(LineColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(line);
        return true;
    }

    // Before the first pick: a small post at the aim point. After it: a line from the first pick to the aim point.
    public override Node3D CreatePreview() => new MeshInstance3D
    {
        Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1f, RadialSegments = 8 },
        MaterialOverride = SpellFx.Glow(LineColor),
    };

    public override void UpdatePreview(Node3D preview, SpellContext ctx, bool valid)
    {
        Vector3 lift = Vector3.Up * Mathf.Max(HeightAboveGround, 0.5f);
        Vector3 start = ctx.Points.Length > 0 ? ctx.Points[^1] + lift : ctx.AimPoint;

        preview.Visible = ctx.AimHit;
        SpellFx.StretchBetween(preview, start, ctx.AimPoint + lift);

        var material = (StandardMaterial3D)((MeshInstance3D)preview).MaterialOverride;
        material.AlbedoColor = valid ? LineColor : InvalidColor;
    }
}
