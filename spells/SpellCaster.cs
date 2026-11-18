using Godot;
using System.Collections.Generic;

public partial class SpellCaster : Node
{
    [Export] public Godot.Collections.Array<SpellDefinition> Spells = new();

    public int ActiveIndex { get; private set; }
    public SpellDefinition ActiveSpell => Spells.Count > 0 ? Spells[ActiveIndex] : null;
    public bool IsHolding => _heldCastId >= 0;     // holding cast on a Hold or Channel spell
    public bool IsPicking => _points.Count > 0;    // part way through a MultiPoint cast

    // Right click belongs to the spell (alt fire) instead of aiming
    public bool AimReplaced => ActiveSpell != null && ActiveSpell.AltMode != AltMode.None;

    // Owner only: what a targeted spell would hit right now (the HUD marks it)
    public Node3D CurrentTarget { get; private set; }

    private const float DefaultTargetRange = 40f;   // for targeted spells with Range 0
    private const float TargetPadding = 0.6f;       // roughly a body's radius, added to AssistRadius

    public event System.Action Casted;   // fired when a cast is paid for; the HUD listens

    private PlayerController _player;
    private ModifierStack _mods;
    private Mana _mana;
    private Health _health;
    private float[] _charges;

    // Owner only
    private int _heldCastId = -1;
    private int _nextCastId;
    private readonly List<Vector3> _points = new();   // MultiPoint picks so far
    private float _healthDebt;                         // health-cost channels pay in whole points

    // Every machine: casts still running, by cast id
    private readonly Dictionary<int, SpellContext> _active = new();

    private Node3D _preview;
    private SpellDefinition _previewSpell;
    private MeshInstance3D _rangeBubble;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _mods = _player.GetNode<ModifierStack>("Modifiers");
        _mana = _player.GetNodeOrNull<Mana>("Mana");
        _health = Health.Of(_player);

        _charges = new float[Spells.Count];
        for (int i = 0; i < Spells.Count; i++)
            _charges[i] = MaxChargesOf(i);

        _rangeBubble = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Front,
                AlbedoColor = new Color(0.6f, 0.8f, 1f, 0.07f),
            },
            Visible = false,
        };
        AddChild(_rangeBubble);
    }

    private bool IsLocal => _player.IsMultiplayerAuthority();

    // ---------- Stats ----------

    public float Stat(SpellDefinition spell, string stat) => _mods.Get(spell, stat);

    public int MaxChargesOf(int index) =>
        Mathf.Max(1, Mathf.RoundToInt(Stat(Spells[index], nameof(SpellDefinition.MaxCharges))));

    private float RechargeTimeOf(int index) =>
        Mathf.Max(0.05f, Stat(Spells[index], nameof(SpellDefinition.RechargeTime)));

    public float ChargesOf(int index) => _charges[index];

    public float CostOf(SpellDefinition spell) =>
        Mathf.Max(0f, Stat(spell, nameof(SpellDefinition.Cost)));

    // The running cast of this spell, if any
    public SpellContext RunningCastOf(int index)
    {
        foreach (SpellContext ctx in _active.Values)
            if (ctx.SpellIndex == index) return ctx;
        return null;
    }

    // ---------- Paying ----------

    // Health costs are always payable: paying more HP than you have kills you
    private bool CanPay(SpellDefinition spell) =>
        spell.PaysWith == CostType.Health || _mana == null || _mana.CanAfford(CostOf(spell));

    // Uses a charge and pays the upfront cost
    private void Spend(int index)
    {
        SpellDefinition spell = Spells[index];
        float cost = CostOf(spell);

        _charges[index] -= 1f;
        if (spell.PaysWith == CostType.Health) _health?.PayCost(cost);
        else _mana?.Spend(cost);
        Casted?.Invoke();
    }

    // Channel upkeep. Returns false when the caster can't keep paying.
    private bool Drain(SpellDefinition spell, float amount)
    {
        if (spell.PaysWith == CostType.Health)
        {
            // Paying every frame would mean an RPC every frame, so pay in whole points
            _healthDebt += amount;
            if (_healthDebt >= 1f)
            {
                float whole = Mathf.Floor(_healthDebt);
                _healthDebt -= whole;
                _health?.PayCost(whole);
            }
            return true;
        }
        return _mana == null || _mana.Drain(amount);
    }

    private SpellContext BuildContext(SpellDefinition spell, bool alt = false)
    {
        SpellContext ctx = _player.BuildSpellContext();
        ctx.Spell = spell;
        ctx.Mods = _mods;
        ctx.SpellIndex = ActiveIndex;
        ctx.Points = _points.ToArray();
        ctx.IsAlt = alt;

        if (alt && spell.AltMode == AltMode.SelfCast) ctx.Target = _player;
        else if (spell.RequiresTarget) ctx.Target = FindTarget(spell);

        if (spell.SnapsToAnchors) ApplySnap(ctx);
        return ctx;
    }

    // ---------- Targeting (owner) ----------

    private float TargetRange(SpellDefinition spell)
    {
        float range = Stat(spell, nameof(SpellDefinition.Range));
        return range > 0f ? range : DefaultTargetRange;
    }

    private bool IsValidTarget(SpellDefinition spell, Node3D body)
    {
        if (body == _player || Health.Of(body) == null) return false;
        TargetFilter needed = Teams.AreAllies(_player, body) ? TargetFilter.Allies : TargetFilter.Enemies;
        return (spell.Targets & needed) != 0;
    }

    private Node3D FindTarget(SpellDefinition spell) =>
        FindAlongAim(TargetRange(spell), Stat(spell, nameof(SpellDefinition.AssistRadius)),
            SpellFx.CharacterLayer, body => IsValidTarget(spell, body));

    private void ApplySnap(SpellContext ctx)
    {
        Node3D found = FindAlongAim(TargetRange(ctx.Spell), Stat(ctx.Spell, nameof(SpellDefinition.AssistRadius)),
            SpellFx.StructureLayer, body => body is ISnapTarget);
        if (found is not ISnapTarget snap) return;

        ctx.AimPoint = snap.SnapPoint;
        ctx.AimNormal = Vector3.Up;
        ctx.AimHit = true;
        ctx.AimCollider = found;
        ctx.AimDirection = (ctx.AimPoint - ctx.CastOrigin).Normalized();
        ctx.Snapped = true;
    }

    // Aim assist: the valid body closest to the crosshair's line, within range and in line of sight
    private Node3D FindAlongAim(float range, float assist, uint mask, System.Func<Node3D, bool> valid)
    {
        Vector3 from = _player.AimFrom;
        Vector3 dir = _player.AimForward;
        var space = _player.GetWorld3D().DirectSpaceState;

        Node3D best = null;
        float bestScore = float.MaxValue;
        foreach (Node3D body in SpellFx.BodiesInSphere(_player, _player.GlobalPosition, range, mask))
        {
            if (!valid(body)) continue;

            Vector3 toBody = body.GlobalPosition - from;
            float along = toBody.Dot(dir);
            if (along <= 0f) continue;                          // behind the camera

            float off = (toBody - dir * along).Length();        // distance from the crosshair's line
            if (off > assist + TargetPadding) continue;

            var sight = PhysicsRayQueryParameters3D.Create(from, body.GlobalPosition, SpellFx.WorldLayer);
            if (space.IntersectRay(sight).Count > 0) continue;  // terrain in the way

            float score = off + along * 0.01f;                  // closest to the line wins; nearer breaks ties
            if (score < bestScore)
            {
                bestScore = score;
                best = body;
            }
        }
        return best;
    }

    // ---------- Selection ----------

    public void SelectSpell(int index)
    {
        if (index < 0 || index >= Spells.Count || index == ActiveIndex) return;
        CancelHeld();
        _points.Clear();
        ActiveIndex = index;
    }

    // ---------- Every frame ----------

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Every machine: running casts tick, and timed ones run out
        foreach (SpellContext ctx in new List<SpellContext>(_active.Values))
        {
            ctx.HeldTime += dt;
            ctx.Spell.Update(ctx, dt);

            bool timed = ctx.Spell.Mode == CastMode.Duration || ctx.Spell.Mode == CastMode.Delayed;
            if (!timed || !_active.ContainsKey(ctx.CastId)) continue;

            ctx.Remaining -= dt;
            if (ctx.Remaining <= 0f) Finish(ctx.CastId);
        }

        if (!IsLocal) return;   // upkeep, charges, preview and HUD are only for this machine's own player

        UpdateChannel(dt);

        SpellDefinition active = ActiveSpell;
        CurrentTarget = active != null && active.RequiresTarget && !_player.MenuOpen ? FindTarget(active) : null;

        for (int i = 0; i < Spells.Count; i++)
        {
            int max = MaxChargesOf(i);
            if (_charges[i] > max) _charges[i] = max;
            else if (_charges[i] < max && !(Spells[i].RechargeAfterEnd && RunningCastOf(i) != null))
                _charges[i] = Mathf.Min(_charges[i] + dt / RechargeTimeOf(i), max);
        }

        UpdatePreview();
    }

    private void UpdateChannel(float dt)
    {
        if (_heldCastId < 0 || !_active.TryGetValue(_heldCastId, out SpellContext ctx)) return;
        if (ctx.Spell.Mode != CastMode.Channel) return;

        float perSecond = Mathf.Max(0f, ctx.Stat(nameof(SpellDefinition.CostPerSecond)));
        bool dry = perSecond > 0f && !Drain(ctx.Spell, perSecond * dt);

        float maxTime = ctx.Stat(nameof(SpellDefinition.MaxChannelTime));
        bool timedOut = maxTime > 0f && ctx.HeldTime >= maxTime;

        if (dry || timedOut) ReleaseCast();
    }

    private void UpdatePreview()
    {
        SpellDefinition spell = ActiveSpell;

        if (_previewSpell != spell)
        {
            _preview?.QueueFree();
            _preview = null;
            _previewSpell = spell;
        }

        bool show = spell != null && !IsHolding && (_player.IsAiming || AimReplaced || IsPicking) && !_player.MenuOpen;
        if (!show)
        {
            if (_preview != null) _preview.Visible = false;
            _rangeBubble.Visible = false;
            return;
        }

        if (_preview == null)
        {
            _preview = spell.CreatePreview();
            if (_preview != null) AddChild(_preview);
        }

        SpellContext ctx = BuildContext(spell);
        bool valid = spell.IsTargetValid(ctx);

        if (_preview != null)
        {
            _preview.Visible = true;
            spell.UpdatePreview(_preview, ctx, valid);
        }

        float range = ctx.Stat(nameof(SpellDefinition.Range));
        _rangeBubble.Visible = range > 0f;
        if (_rangeBubble.Visible)
        {
            _rangeBubble.GlobalPosition = _player.GlobalPosition;
            _rangeBubble.Scale = Vector3.One * range;
        }
    }

    // ---------- Casting (owner) ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsLocal || _player.MenuOpen) return;

        if (e.IsActionPressed("cast")) TryCast(false);
        else if (e.IsActionReleased("cast")) ReleaseCast();
        else if (AimReplaced && e.IsActionPressed("aim")) PressAlt();
        else if (AimReplaced && e.IsActionReleased("aim")) ReleaseCast();
    }

    private void PressAlt()
    {
        switch (ActiveSpell.AltMode)
        {
            case AltMode.CancelPicks:
                _points.Clear();
                break;
            case AltMode.SelfCast:
            case AltMode.AltCast:
                TryCast(true);
                break;
        }
    }

    private void TryCast(bool alt)
    {
        SpellDefinition spell = ActiveSpell;
        if (spell == null || IsHolding) return;

        // Pressing again while it's still running: recast instead of casting a new one
        if (!alt && spell.AllowRecast && RunningCastOf(ActiveIndex) is SpellContext running)
        {
            Rpc(MethodName.ReplicateRecast, running.CastId);
            return;
        }

        if (_charges[ActiveIndex] < 1f || !CanPay(spell)) return;

        SpellContext ctx = BuildContext(spell, alt);
        if (spell.RequiresTarget && ctx.Target == null) return;   // nothing under the crosshair
        if (!spell.IsTargetValid(ctx)) return;   // validated here, on the machine that can see the target

        if (spell.Mode == CastMode.MultiPoint)
        {
            _points.Add(ctx.AimPoint);
            if (_points.Count < Mathf.Max(1, spell.PointCount)) return;   // keep picking
            ctx.Points = _points.ToArray();
            _points.Clear();
        }

        int castId = _nextCastId++;
        int index = ActiveIndex;
        NodePath target = ctx.Target != null ? ctx.Target.GetPath() : new NodePath("");
        Rpc(MethodName.ReplicateBegin, index, castId,
            ctx.CastOrigin, ctx.AimPoint, ctx.AimNormal, ctx.AimDirection, ctx.AimHit, ctx.Points, alt, target);

        if (spell.Mode == CastMode.Hold || spell.Mode == CastMode.Channel) _heldCastId = castId;
        if (spell.Mode != CastMode.Hold) Spend(index);   // Hold pays when released
    }

    private void ReleaseCast()
    {
        if (_heldCastId < 0) return;

        int castId = _heldCastId;
        _heldCastId = -1;
        if (!_active.TryGetValue(castId, out SpellContext ctx)) return;

        if (ctx.Spell.Mode == CastMode.Hold) Spend(ctx.SpellIndex);
        Rpc(MethodName.ReplicateRelease, castId, ctx.HeldTime);
    }

    private void CancelHeld()
    {
        if (_heldCastId < 0) return;

        Rpc(MethodName.ReplicateCancel, _heldCastId);
        _heldCastId = -1;
    }

    // For spells that end themselves early on the owner's machine (a buff broken, a target lost)
    public void EndCast(SpellContext ctx)
    {
        if (IsLocal && _active.ContainsKey(ctx.CastId)) Rpc(MethodName.ReplicateFinish, ctx.CastId);
    }

    // ---------- Casting (every machine) ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateBegin(int spellIndex, int castId, Vector3 origin, Vector3 aimPoint,
                                Vector3 aimNormal, Vector3 aimDirection, bool aimHit, Vector3[] points,
                                bool alt, NodePath targetPath)
    {
        if (spellIndex < 0 || spellIndex >= Spells.Count) return;
        SpellDefinition spell = Spells[spellIndex];

        var ctx = new SpellContext
        {
            Caster = _player,
            Spell = spell,
            Mods = _mods,
            SpellIndex = spellIndex,
            CastId = castId,
            Authoritative = Multiplayer.IsServer(),
            CastOrigin = origin,
            AimPoint = aimPoint,
            AimNormal = aimNormal,
            AimDirection = aimDirection,
            AimHit = aimHit,
            Points = points,
            IsAlt = alt,
            // Targets are sent as node paths: the same path finds the same entity on every machine
            Target = targetPath.IsEmpty ? null : GetNodeOrNull<Node3D>(targetPath),
        };
        if (!targetPath.IsEmpty && ctx.Target == null) return;   // the target is already gone here

        if (spell.Mode == CastMode.Duration) ctx.Remaining = ctx.Stat(nameof(SpellDefinition.Duration));
        if (spell.Mode == CastMode.Delayed) ctx.Remaining = ctx.Stat(nameof(SpellDefinition.Delay));

        if (!spell.Begin(ctx)) return;

        bool runs = spell.Mode != CastMode.Instant && spell.Mode != CastMode.MultiPoint;
        if (runs) _active[castId] = ctx;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateRelease(int castId, float heldTime)
    {
        if (!_active.Remove(castId, out SpellContext ctx)) return;
        ctx.HeldTime = heldTime;   // the owner's held time, so a charge-up resolves the same everywhere
        ctx.Spell.Release(ctx);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateCancel(int castId)
    {
        if (_active.Remove(castId, out SpellContext ctx)) ctx.Spell.Cancel(ctx);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateRecast(int castId)
    {
        if (!_active.TryGetValue(castId, out SpellContext ctx)) return;
        ctx.Spell.Recast(ctx);
        Finish(castId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateFinish(int castId) => Finish(castId);

    private void Finish(int castId)
    {
        if (_active.Remove(castId, out SpellContext ctx)) ctx.Spell.Expire(ctx);
    }
}
