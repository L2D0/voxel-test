using Godot;
using System.Collections.Generic;

// Shock's basic attack: a bolt at the target under the crosshair that jumps to nearby enemies,
// losing a little power with each jump.
[GlobalClass]
public partial class ArcBoltSpell : SpellDefinition
{
    [ExportGroup("Arc Bolt")]
    [Export] public float Damage = 18f;
    [Export] public int ChainCount = 3;           // extra jumps after the first target
    [Export] public float ChainRange = 7f;        // how far each jump can reach
    [Export] public float ChainFalloff = 0.8f;    // damage multiplier per jump
    [Export] public bool ChainNeedsSight = true;  // jumps can't pass through terrain
    [Export] public Color BoltColor = new(0.7f, 0.85f, 1f, 0.95f);

    public ArcBoltSpell()
    {
        SpellName = "Arc Bolt";
        Element = Element.Shock;
        Mode = CastMode.Instant;
        Range = 25f;
        MaxCharges = 2;
        RechargeTime = 0.8f;
        Cost = 12f;
        RequiresTarget = true;
        Targets = TargetFilter.Enemies;
    }

    // Runs on every machine, but only the host routes the chain and deals damage.
    // Everyone (host included) draws it when the host's route arrives in ShowChain.
    public override bool Begin(SpellContext ctx)
    {
        if (ctx.Target == null) return false;
        if (!ctx.Authoritative) return true;

        int extra = Mathf.Max(0, Mathf.RoundToInt(ctx.Stat(nameof(ChainCount))));
        List<Node3D> hops = Chain.Route(ctx.Caster, ctx.Target, extra, ctx.Stat(nameof(ChainRange)),
            ctx.Spell.Targets, ChainNeedsSight);

        float damage = ctx.Stat(nameof(Damage));
        float falloff = ctx.Stat(nameof(ChainFalloff));
        foreach (Node3D hop in hops)
        {
            Health.Of(hop)?.TakeDamage(DamageInfo.FromSpell(ctx, damage));
            ctx.ApplyStatuses(hop);
            damage *= falloff;
        }

        ctx.Casting.BroadcastChain(ctx, hops);
        return true;
    }

    public override void ShowChain(SpellContext ctx, Node3D[] hops)
    {
        Vector3 from = ctx.CastOrigin;
        foreach (Node3D hop in hops)
        {
            Vector3 to = hop.GlobalPosition;
            Zigzag(ctx.Caster, from, to);
            SpellFx.Flash(ctx.Caster, to, 0.7f, new Color(BoltColor, 0.5f));
            from = to;
        }
    }

    // A bolt broken into a few jittered segments so it reads as lightning, not a laser
    private void Zigzag(Node from, Vector3 a, Vector3 b)
    {
        const int segments = 4;
        Vector3 dir = (b - a).Normalized();
        Vector3 side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() < 0.01f) side = dir.Cross(Vector3.Right);
        side = side.Normalized();
        Vector3 up = side.Cross(dir);

        Vector3 previous = a;
        for (int i = 1; i <= segments; i++)
        {
            Vector3 point = a.Lerp(b, i / (float)segments);
            if (i < segments)
                point += side * (float)GD.RandRange(-0.35, 0.35) + up * (float)GD.RandRange(-0.35, 0.35);

            SpellFx.Line(from, previous, point, BoltColor, 0.18f, 0.035f);
            previous = point;
        }
    }
}
