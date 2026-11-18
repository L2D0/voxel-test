using Godot;

// The line a Tether leaves behind. Every machine shows it; the host damages whatever crosses it.
public partial class TetherLine : MeshInstance3D
{
    public SpellContext Ctx;
    public Vector3 A, B;
    public float Lifetime = 5f;
    public float DamagePerTick = 8f;
    public float TickInterval = 0.5f;

    private float _age, _tickTimer;

    public override void _Ready() => SpellFx.StretchBetween(this, A, B);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }

        if (!Ctx.Authoritative) return;
        _tickTimer -= dt;
        if (_tickTimer > 0f) return;
        _tickTimer = TickInterval;

        // A ray along the line catches the first thing standing in it
        var query = PhysicsRayQueryParameters3D.Create(A, B, SpellFx.DamageableMask);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].As<Node>() is not Node body) return;
        if (Teams.AreAllies(Ctx.Caster, body)) return;

        Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(Ctx, DamagePerTick));
        Ctx.ApplyStatuses(body);
    }
}
