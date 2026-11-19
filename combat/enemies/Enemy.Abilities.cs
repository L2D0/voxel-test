using Godot;

// How an enemy uses its abilities. The host picks one, winds up (everyone sees the telegraph),
// then tells every machine to cast it, the same way a player's cast is replicated: every machine
// runs the spell for its visuals, and only the host's copy deals damage.
public partial class Enemy : ICaster
{
    private static readonly Color TelegraphColor = new(1f, 0.35f, 0.1f);

    private float[] _cooldowns = System.Array.Empty<float>();
    private int _windingUp = -1;     // which ability is winding up; -1 = none
    private float _windupTimer;
    private float _recoveryTimer;
    private int _nextCastId;
    private bool _telegraphing;      // every machine: tinted while winding up

    private bool Busy => _windingUp >= 0 || _recoveryTimer > 0f;

    // ---------- ICaster ----------

    public Vector3 LiveCastOrigin => GlobalPosition + Vector3.Up * EyeHeight - GlobalBasis.Z * 0.5f;

    public Vector3 LiveAimDirection =>
        _target != null && IsInstanceValid(_target)
            ? (_target.GlobalPosition - GlobalPosition).Normalized()
            : -GlobalBasis.Z;

    // ---------- Host ----------

    // Every frame, before the state runs
    private void TickAbilities(float dt)
    {
        if (_cooldowns.Length != Def.Abilities.Count) _cooldowns = new float[Def.Abilities.Count];
        for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] -= dt;
        _recoveryTimer -= dt;

        if (_windingUp < 0) return;

        // Launched mid-windup: the attack is lost
        if (_launchTimer > 0f || !IsOnFloor())
        {
            CancelWindup();
            return;
        }

        Stop();
        if (_target != null && IsInstanceValid(_target)) LookToward(_target.GlobalPosition);

        _windupTimer -= dt;
        if (_windupTimer <= 0f) Fire();
    }

    // How close the chase should get: just inside the longest reach it can use right now
    private float PreferredRange()
    {
        float best = 0f;
        foreach (EnemyAbility ability in Def.Abilities)
            if (ability?.Spell != null && HealthAllows(ability)) best = Mathf.Max(best, ability.MaxRange);
        return best > 0f ? best * 0.8f : Def.StopDistance;
    }

    private bool HealthAllows(EnemyAbility ability) => _health.Current <= _health.Max * ability.BelowHealth;

    // Picks a ready ability by weight and starts winding it up. False if nothing fits.
    private bool TryStartAbility(float distance, bool seen)
    {
        if (Busy || _target == null) return false;

        float total = 0f;
        for (int i = 0; i < Def.Abilities.Count; i++)
            if (Usable(i, distance, seen)) total += Def.Abilities[i].Weight;
        if (total <= 0f) return false;

        float roll = _rng.Randf() * total;
        for (int i = 0; i < Def.Abilities.Count; i++)
        {
            if (!Usable(i, distance, seen)) continue;
            roll -= Def.Abilities[i].Weight;
            if (roll > 0f) continue;

            _windingUp = i;
            _windupTimer = Def.Abilities[i].Windup;
            Rpc(MethodName.ShowTelegraph, true);
            return true;
        }
        return false;
    }

    private bool Usable(int i, float distance, bool seen)
    {
        EnemyAbility ability = Def.Abilities[i];
        return ability?.Spell != null && ability.Weight > 0f
            && _cooldowns[i] <= 0f
            && distance >= ability.MinRange && distance <= ability.MaxRange
            && (seen || !ability.NeedsSight)
            && HealthAllows(ability);
    }

    private void CancelWindup()
    {
        if (_windingUp < 0) return;
        _windingUp = -1;
        Rpc(MethodName.ShowTelegraph, false);
    }

    private void Fire()
    {
        int index = _windingUp;
        EnemyAbility ability = Def.Abilities[index];
        _windingUp = -1;
        _cooldowns[index] = ability.Cooldown;
        _recoveryTimer = ability.Recovery;

        // Aim where the target is now, not where it was when the windup started: dodging means moving
        Vector3 origin = LiveCastOrigin;
        Vector3 aimPoint = _target != null && IsInstanceValid(_target) ? _target.GlobalPosition : origin - GlobalBasis.Z * 5f;
        Vector3 direction = (aimPoint - origin).Normalized();
        NodePath targetPath = _target != null && IsInstanceValid(_target) ? _target.GetPath() : new NodePath();

        Rpc(MethodName.CastAbility, index, _nextCastId++, origin, aimPoint, direction, targetPath);
    }

    // ---------- Every machine ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShowTelegraph(bool on)
    {
        _telegraphing = on;
        if (!_health.IsDead) _material.AlbedoColor = BodyColor();
    }

    // Instant and Hold spells for now. Hold fires at full charge.
    // Channel, Duration and Delayed come with bosses, when something needs them.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void CastAbility(int index, int castId, Vector3 origin, Vector3 aimPoint, Vector3 direction, NodePath targetPath)
    {
        ShowTelegraph(false);
        if (_health.IsDead || index < 0 || index >= Def.Abilities.Count) return;
        SpellDefinition spell = Def.Abilities[index]?.Spell;
        if (spell == null) return;

        var ctx = new SpellContext
        {
            Caster = this,
            Spell = spell,
            Mods = null,                 // enemies have no spell modifiers yet
            SpellIndex = index,
            CastId = castId,
            Authoritative = Multiplayer.IsServer(),
            CastOrigin = origin,
            AimPoint = aimPoint,
            AimNormal = Vector3.Up,
            AimDirection = direction,
            AimHit = true,
            Target = targetPath.IsEmpty ? null : GetNodeOrNull<Node3D>(targetPath),
        };

        if (!spell.Begin(ctx)) return;
        if (spell.Mode == CastMode.Hold)
        {
            ctx.HeldTime = ctx.Stat(nameof(SpellDefinition.ChargeTime));
            spell.Release(ctx);
        }
    }

    // The colour the body rests at right now
    private Color BodyColor()
    {
        Color tint = Def?.Tint ?? Colors.Gray;
        if (_health.IsDead) return tint.Darkened(0.5f);
        return _telegraphing ? TelegraphColor : tint;
    }
}
