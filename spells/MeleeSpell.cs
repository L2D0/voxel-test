using Godot;

// A swing in front of the caster: everything inside the arc and within reach gets hit once.
// Enemy bites and claws use it now; Bloodrite, Ivy whip and bear form will later.
[GlobalClass]
public partial class MeleeSpell : SpellDefinition
{
    [ExportGroup("Melee")]
    [Export] public float Damage = 10f;
    [Export] public float Reach = 2.2f;                                  // from the caster's centre
    [Export(PropertyHint.Range, "10,360")] public float Arc = 100f;     // the whole arc; 360 = all around
    [Export] public float Knockback = 3f;
    [Export] public float KnockUp = 1.5f;
    [Export] public Color SwingColor = new(1f, 0.9f, 0.8f, 0.6f);

    private const float BodyAllowance = 0.5f;   // reach is to a body's centre; this lets a capsule's edge count
    private const float MaxHeightGap = 2f;      // doesn't hit someone standing on a pillar overhead

    public override bool Begin(SpellContext ctx)
    {
        Vector3 forward = Flat(ctx.AimDirection);
        if (forward.LengthSquared() < 0.001f) forward = Flat(-ctx.Caster.GlobalBasis.Z);
        forward = forward.Normalized();

        float reach = ctx.Stat(nameof(Reach));
        float arc = ctx.Stat(nameof(Arc));
        ShowSwing(ctx, forward, reach, arc);

        if (!ctx.Authoritative) return true;   // the host decides who got hit

        float minDot = Mathf.Cos(Mathf.DegToRad(arc * 0.5f));
        float damage = ctx.Stat(nameof(Damage));
        Vector3 push = forward * ctx.Stat(nameof(Knockback)) + Vector3.Up * ctx.Stat(nameof(KnockUp));
        Vector3 origin = ctx.Caster.GlobalPosition;

        foreach (Node3D body in SpellFx.BodiesInSphere(ctx.Caster, origin, reach + BodyAllowance, SpellFx.CharacterLayer))
        {
            if (body == ctx.Caster) continue;
            // Allies are safe, except the one it's aiming at (a predator biting prey of its own side)
            if (body != ctx.Target && !Teams.CanHarm(ctx.Caster, body)) continue;
            if (Mathf.Abs(body.GlobalPosition.Y - origin.Y) > MaxHeightGap) continue;

            Vector3 to = Flat(body.GlobalPosition - origin);
            if (to.LengthSquared() > 0.01f && forward.Dot(to.Normalized()) < minDot) continue;

            Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(ctx, damage));
            ctx.ApplyStatuses(body);
            if (body is IImpulseReceiver receiver) receiver.AddImpulse(push);
        }
        return true;
    }

    // Placeholder: a fading line traced along the arc at the edge of its reach
    private void ShowSwing(SpellContext ctx, Vector3 forward, float reach, float arc)
    {
        const int segments = 8;
        Vector3 centre = ctx.Caster.GlobalPosition;
        float half = Mathf.DegToRad(arc * 0.5f);

        Vector3 previous = Vector3.Zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (float)segments);
            Vector3 point = centre + forward.Rotated(Vector3.Up, angle) * reach;
            if (i > 0) SpellFx.Line(ctx.Caster, previous, point, SwingColor, 0.2f, 0.04f);
            previous = point;
        }
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
