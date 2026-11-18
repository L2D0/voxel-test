using Godot;

// Duration test: a self buff. Recharges only after it ends, and pressing cast again ends it early.
[GlobalClass]
public partial class KindleTestSpell : SpellDefinition
{
    [ExportGroup("Kindle")]
    [Export] public Godot.Collections.Array<StatModifier> Buffs = new();   // active for the duration
    [Export] public Color GlowColor = new(1f, 0.55f, 0.2f, 0.18f);

    public KindleTestSpell()
    {
        SpellName = "Test Kindle";
        Element = Element.Fire;
        Mode = CastMode.Duration;
        Duration = 6f;
        RechargeTime = 4f;
        RechargeAfterEnd = true;
        AllowRecast = true;
    }

    public override bool Begin(SpellContext ctx)
    {
        // Every machine adds the buff to its copy of the caster, so everyone computes the same stats
        foreach (StatModifier mod in Buffs)
            if (mod != null) ctx.Mods.Add(mod, ctx);

        var glow = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.9f, Height = 1.8f },
            MaterialOverride = SpellFx.Glow(GlowColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ctx.Caster.AddChild(glow);   // follows the caster
        ctx.Instance = glow;
        return true;
    }

    public override void Expire(SpellContext ctx) => End(ctx);
    public override void Cancel(SpellContext ctx) => End(ctx);

    private static void End(SpellContext ctx)
    {
        ctx.Mods.RemoveSource(ctx);
        ctx.Instance?.QueueFree();
    }
}
