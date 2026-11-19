using Godot;

// Delayed test: mark a spot, it lands after Delay seconds.
[GlobalClass]
public partial class FlareTestSpell : SpellDefinition
{
    [ExportGroup("Flare")]
    [Export] public float Radius = 3.5f;
    [Export] public float Damage = 40f;
    [Export] public Color MarkColor = new(1f, 0.85f, 0.4f, 0.5f);

    private static readonly Color InvalidColor = new(1f, 0.35f, 0.3f, 0.3f);

    public FlareTestSpell()
    {
        SpellName = "Test Flare";
        Element = Element.Fire;
        Mode = CastMode.Delayed;
        Delay = 1.2f;
        Range = 30f;
        RechargeTime = 3f;
    }

    public override bool IsTargetValid(SpellContext ctx) =>
        ctx.AimHit && ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= ctx.Stat(nameof(Range));

    public override bool Begin(SpellContext ctx)
    {
        MeshInstance3D ring = MakeRing(ctx.Stat(nameof(Radius)), MarkColor);
        ctx.Caster.GetTree().CurrentScene.AddChild(ring);
        ring.GlobalPosition = ctx.AimPoint + Vector3.Up * 0.05f;
        ctx.Instance = ring;
        return true;
    }

    // The ring tightens as it's about to land
    public override void Update(SpellContext ctx, float dt)
    {
        if (ctx.Instance == null) return;
        float delay = Mathf.Max(ctx.Stat(nameof(Delay)), 0.01f);
        float left = Mathf.Clamp(ctx.Remaining / delay, 0f, 1f);
        ctx.Instance.Scale = new Vector3(1f + left * 0.5f, 1f, 1f + left * 0.5f);
    }

    public override void Expire(SpellContext ctx)
    {
        ctx.Instance?.QueueFree();

        float radius = ctx.Stat(nameof(Radius));
        SpellFx.Flash(ctx.Caster, ctx.AimPoint, radius, new Color(1f, 0.85f, 0.4f, 0.6f));

        if (!ctx.Authoritative) return;   // the host decides who got hit
        foreach (Node3D body in SpellFx.BodiesInSphere(ctx.Caster, ctx.AimPoint, radius))
        {
            if (!Teams.CanHarm(ctx.Caster, body)) continue;
            Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(ctx, ctx.Stat(nameof(Damage))));
            ctx.ApplyStatuses(body);
        }
    }

    public override void Cancel(SpellContext ctx) => ctx.Instance?.QueueFree();

    public override Node3D CreatePreview() => MakeRing(Radius, MarkColor);

    public override void UpdatePreview(Node3D preview, SpellContext ctx, bool valid)
    {
        preview.Visible = ctx.AimHit;
        preview.GlobalPosition = ctx.AimPoint + Vector3.Up * 0.05f;

        float scale = ctx.Stat(nameof(Radius)) / Radius;   // follows radius modifiers
        preview.Scale = new Vector3(scale, 1f, scale);

        var material = (StandardMaterial3D)((MeshInstance3D)preview).MaterialOverride;
        material.AlbedoColor = valid ? MarkColor : InvalidColor;
    }

    private static MeshInstance3D MakeRing(float radius, Color color) => new()
    {
        Mesh = new TorusMesh { InnerRadius = Mathf.Max(radius - 0.15f, 0.05f), OuterRadius = radius },
        MaterialOverride = SpellFx.Glow(color),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
