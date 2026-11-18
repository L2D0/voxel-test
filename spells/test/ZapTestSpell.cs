using Godot;

// Targeted test: needs an entity under the crosshair. Damages enemies, heals allies,
// and right click casts it on yourself.
[GlobalClass]
public partial class ZapTestSpell : SpellDefinition
{
    [ExportGroup("Zap")]
    [Export] public float Damage = 20f;
    [Export] public float Healing = 15f;
    [Export] public Color HarmColor = new(0.75f, 0.85f, 1f, 0.9f);
    [Export] public Color HealColor = new(0.7f, 1f, 0.7f, 0.9f);

    public ZapTestSpell()
    {
        SpellName = "Test Zap";
        Element = Element.Shock;
        Mode = CastMode.Instant;
        Range = 25f;
        RechargeTime = 0.6f;
        MaxCharges = 2;
        RequiresTarget = true;
        Targets = TargetFilter.Enemies | TargetFilter.Allies;
        AltMode = AltMode.SelfCast;
    }

    public override bool Begin(SpellContext ctx)
    {
        Node3D target = ctx.Target;
        if (target == null) return false;

        bool friendly = Teams.AreAllies(ctx.Caster, target);
        Color color = friendly ? HealColor : HarmColor;

        if (target != ctx.Caster) SpellFx.Line(ctx.Caster, ctx.CastOrigin, target.GlobalPosition, color);
        SpellFx.Flash(ctx.Caster, target.GlobalPosition, 0.8f, new Color(color, 0.5f));

        if (!ctx.Authoritative) return true;   // the host changes health

        if (friendly)
        {
            Health.Of(target)?.Heal(ctx.Stat(nameof(Healing)));
        }
        else
        {
            Health.Of(target)?.TakeDamage(DamageInfo.FromSpell(ctx, ctx.Stat(nameof(Damage))));
            ctx.ApplyStatuses(target);
        }
        return true;
    }
}
