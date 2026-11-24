using Godot;
using System.Collections.Generic;

// Casts the player's spells. Spells is the current loadout: the spells socketed in the equipped focus.
// No focus, no spells. The host decides the loadout and tells every machine, because casts are replicated by
// their index in this list. The scene's Spells list is only the starting set of known spells.
public partial class SpellCaster : Node
{
    [Export] public Godot.Collections.Array<SpellDefinition> Spells = new();

    // Host: every spell this player has learned, and what's socketed in each focus they've used
    private readonly List<string> _known = new();
    private readonly Dictionary<string, string[]> _sockets = new();
    private HeldItem _held;          // the focus model in the right hand
    private string _focusId = "";

    public int ActiveIndex { get; private set; }
    public SpellDefinition ActiveSpell => Spells.Count > 0 ? Spells[ActiveIndex] : null;
    public bool IsHolding => _heldCastId >= 0;     // holding cast on a Hold or Channel spell
    public bool IsPicking => _points.Count > 0;    // part way through a MultiPoint cast

    // Holding a spell that keeps the player turned toward the camera (beams, charged throws)
    public bool LocksFacing =>
        _heldCastId >= 0 && _active.TryGetValue(_heldCastId, out SpellContext held) && held.Spell.FaceAimWhileHeld;

    // Holding a spell that needs the caster's feet on the ground: no jumping until it's released
    public bool LocksJump =>

        _heldCastId >= 0 && _active.TryGetValue(_heldCastId, out SpellContext held) && held.Spell.RequiresGround;
    
    // Holding a spell that roots the caster: no moving or jumping until it's released
    public bool LocksMovement =>
        _heldCastId >= 0 && _active.TryGetValue(_heldCastId, out SpellContext held) && held.Spell.RootsWhileHeld;


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
    private readonly List<Vector3> _points = new();       // MultiPoint picks so far
    private readonly List<Node3D> _pickTargets = new();   // what each pick snapped to
    private float _healthDebt;                         // health-cost channels pay in whole points

    // Every machine: casts still running, by cast id
    private readonly Dictionary<int, SpellContext> _active = new();

    // Every machine: objects each spell has spawned, oldest first (for MaxActive)
    private readonly Dictionary<int, List<Node>> _owned = new();

    private Node3D _preview;
    private SpellDefinition _previewSpell;
    private MeshInstance3D _rangeBubble;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _mods = _player.GetNode<ModifierStack>("Modifiers");
        _mana = _player.GetNodeOrNull<Mana>("Mana");
        _health = Health.Of(_player);

        // The scene's list is what you start out knowing; nothing is castable until a focus is equipped
        foreach (SpellDefinition spell in Spells)
            if (spell != null && !string.IsNullOrEmpty(spell.ResourcePath)) _known.Add(spell.ResourcePath);
        Spells = new Godot.Collections.Array<SpellDefinition>();
        _charges = System.Array.Empty<float>();

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

        _held = new HeldItem { Name = "Hand", Position = HeldItem.HandOffset };
        _player.GetNode<Node3D>("Model").AddChild(_held);
    }

    private bool IsLocal => _player.IsMultiplayerAuthority();

    // ---------- Stats ----------

    public float Stat(SpellDefinition spell, string stat) => _mods.Get(spell, stat);

    public int MaxChargesOf(int index) =>
        Mathf.Max(1, Mathf.RoundToInt(Stat(Spells[index], nameof(SpellDefinition.MaxCharges))));

    private float RechargeTimeOf(int index) =>
    Mathf.Max(0.05f, Stat(Spells[index], nameof(SpellDefinition.RechargeTime))
                     / Mathf.Max(0.1f, _mods.GetPlayer(PlayerStat.RechargeRate, 1f)));

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
        ctx.PointTargets = _pickTargets.ToArray();
        ctx.IsAlt = alt;

        if (alt && spell.AltMode == AltMode.SelfCast) ctx.Target = _player;
        else if (spell.RequiresTarget) ctx.Target = FindTarget(spell);

        if (spell.SnapsTo != SnapKind.None) ApplySnap(ctx);
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
        return Teams.Matches(_player, body, spell.Targets);
    }

    private Node3D FindTarget(SpellDefinition spell) =>
        FindAlongAim(TargetRange(spell), Stat(spell, nameof(SpellDefinition.AssistRadius)),
             SpellFx.DamageableMask, body => IsValidTarget(spell, body));   // characters and structures

    private void ApplySnap(SpellContext ctx)
    {
        Node3D found = FindAlongAim(TargetRange(ctx.Spell), Stat(ctx.Spell, nameof(SpellDefinition.AssistRadius)),
            SpellFx.StructureLayer,
            body => body is ISnapTarget s && (s.Kind & ctx.Spell.SnapsTo) != 0,
            body => ((ISnapTarget)body).SnapPoint);   // measure to where it snaps, not to its base
        if (found is not ISnapTarget snap) return;

        ctx.AimPoint = snap.SnapPoint;
        ctx.AimNormal = Vector3.Up;
        ctx.AimHit = true;
        ctx.AimCollider = found;
        ctx.AimDirection = (ctx.AimPoint - ctx.CastOrigin).Normalized();
        ctx.Snapped = true;
    }

    // Aim assist: the valid body closest to the crosshair's line, within range and in line of sight.
    // pointOf says which point on a body to measure to (its origin, unless given).
    private Node3D FindAlongAim(float range, float assist, uint mask, System.Func<Node3D, bool> valid,
                                System.Func<Node3D, Vector3> pointOf = null)
    {
        Vector3 from = _player.AimFrom;
        Vector3 dir = _player.AimForward;
        var space = _player.GetWorld3D().DirectSpaceState;

        Node3D best = null;
        float bestScore = float.MaxValue;
        foreach (Node3D body in SpellFx.BodiesInSphere(_player, _player.GlobalPosition, range, mask))
        {
            if (!valid(body)) continue;

            Vector3 point = pointOf != null ? pointOf(body) : body.GlobalPosition;
            Vector3 toBody = point - from;
            float along = toBody.Dot(dir);
            if (along <= 0f) continue;                          // behind the camera

            float off = (toBody - dir * along).Length();        // distance from the crosshair's line
            if (off > assist + TargetPadding) continue;

            var sight = PhysicsRayQueryParameters3D.Create(from, point, SpellFx.WorldLayer);
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
        ClearPicks();
        ActiveIndex = index;
    }

    private void ClearPicks()
    {
        _points.Clear();
        _pickTargets.Clear();
    }

    // ---------- Foci and the loadout ----------

    // Host: the equipped focus changed (or a save loaded). Work out its spells and tell everyone.
    public void SetFocus(string focusId)
    {
        if (!Multiplayer.IsServer()) return;
        Rpc(MethodName.ReceiveLoadout, LoadoutFor(focusId), focusId ?? "");
    }

    // Host: a focus's spells. A focus nobody has socketed yet gets the first spells you know, in order,
    // until the attunement table exists to choose them.
    private string[] LoadoutFor(string focusId)
    {
        if (ItemDatabase.Get(focusId) is not ItemDefinition focus || focus.SpellSlots <= 0) return System.Array.Empty<string>();

        if (!_sockets.TryGetValue(focusId, out string[] socketed))
        {
            socketed = new string[focus.SpellSlots];
            for (int i = 0; i < socketed.Length; i++) socketed[i] = i < _known.Count ? _known[i] : "";
            _sockets[focusId] = socketed;
        }

        var spells = new List<string>();
        for (int i = 0; i < Mathf.Min(socketed.Length, focus.SpellSlots); i++)
            if (!string.IsNullOrEmpty(socketed[i]) && _known.Contains(socketed[i])) spells.Add(socketed[i]);
        return spells.ToArray();
    }

    // Every machine: swap the loadout. Anything mid-cast is dropped, since spell indices change.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLoadout(string[] paths, string focusId)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 && Multiplayer.GetRemoteSenderId() != 0) return;   // host only (0 = called locally)

        foreach (SpellContext ctx in _active.Values) ctx.Spell.Cancel(ctx);
        _active.Clear();
        _owned.Clear();
        _heldCastId = -1;
        ClearPicks();
        _preview?.QueueFree();
        _preview = null;
        _previewSpell = null;

        var spells = new Godot.Collections.Array<SpellDefinition>();
        foreach (string path in paths)
            if (ResourceLoader.Exists(path) && GD.Load(path) is SpellDefinition spell) spells.Add(spell);
        Spells = spells;
        ActiveIndex = 0;
        _focusId = focusId ?? "";
        _held?.Hold(_focusId);

        _charges = new float[Spells.Count];
        for (int i = 0; i < Spells.Count; i++) _charges[i] = MaxChargesOf(i);
    }

    // Host: a player who just joined needs everyone else's loadout
    public void SendLoadoutTo(long peer)
    {
        var paths = new string[Spells.Count];
        for (int i = 0; i < Spells.Count; i++) paths[i] = Spells[i]?.ResourcePath ?? "";
        RpcId(peer, MethodName.ReceiveLoadout, paths, _focusId);
    }

    // ---------- Known spells (host; saved with the player) ----------

    public string[] KnownSpells() => _known.ToArray();

    public Dictionary<string, string[]> SocketsSnapshot()
    {
        var copy = new Dictionary<string, string[]>();
        foreach (var (focus, spells) in _sockets) copy[focus] = (string[])spells.Clone();
        return copy;
    }

    public void LoadSpells(string[] known, Dictionary<string, string[]> sockets)
    {
        if (!Multiplayer.IsServer()) return;
        if (known != null && known.Length > 0)   // older saves have none: keep the starting set
        {
            _known.Clear();
            foreach (string path in known)
                if (ResourceLoader.Exists(path)) _known.Add(path);
        }
        _sockets.Clear();
        if (sockets != null) foreach (var (focus, spells) in sockets) _sockets[focus] = (string[])spells.Clone();
    }

    // ---------- Spawned objects ----------

    // Every machine runs this for every cast, in the same order, so they all remove the same oldest one
    public void Track(int spellIndex, Node node)
    {
        if (!_owned.TryGetValue(spellIndex, out List<Node> list))
            _owned[spellIndex] = list = new List<Node>();

        list.RemoveAll(n => !IsInstanceValid(n) || n.IsQueuedForDeletion());   // already expired
        list.Add(node);

        int max = Mathf.RoundToInt(Stat(Spells[spellIndex], nameof(SpellDefinition.MaxActive)));
        while (max > 0 && list.Count > max)
        {
            if (list[0] is Extrusion extrusion) extrusion.Crumble();   // earth breaks apart rather than vanishing
            else list[0].QueueFree();
            list.RemoveAt(0);
        }
    }

    // Oldest first: the next one to be replaced is OwnedBy(i)[0]
    public IReadOnlyList<Node> OwnedBy(int spellIndex)
    {
        if (!_owned.TryGetValue(spellIndex, out List<Node> list)) return System.Array.Empty<Node>();
        list.RemoveAll(n => !IsInstanceValid(n) || n.IsQueuedForDeletion());
        return list;
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
        if (LocksJump && !_player.IsOnFloor()) CancelHeld();   // knocked into the air: a grounded cast is lost
        if (_player.IsStunned)
        {
            CancelHeld();    // a stun drops whatever you were holding
            ClearPicks();
        }
        if (IsPicking && !_player.IsAiming) ClearPicks();   // letting go of aim abandons a MultiPoint cast

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

        bool show = spell != null && !IsHolding && _player.IsAiming && !_player.MenuOpen;
        if (!show)
        {
            if (_preview != null) _preview.Visible = false;
            ShowChannelBubble();
            return;
        }

        if (_preview == null)
        {
            _preview = spell.CreatePreview();
            if (_preview != null) AddChild(_preview);
        }

        SpellContext ctx = BuildContext(spell);
        bool valid = spell.IsTargetValid(ctx) && (!spell.RequiresSnap || ctx.Snapped);

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

    // A channel keeps its range bubble while held, so you can see when a target walks out of reach
    private void ShowChannelBubble()
    {
        float range = 0f;
        if (_heldCastId >= 0 && _active.TryGetValue(_heldCastId, out SpellContext held)
            && held.Spell.Mode == CastMode.Channel && _player.IsAiming && !_player.MenuOpen)
            range = held.Stat(nameof(SpellDefinition.Range));

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
        if (!IsLocal || _player.MenuOpen || _player.IsDead || _player.IsStunned) return;

        if (e.IsActionPressed("cast")) TryCast(false);
        else if (e.IsActionReleased("cast")) ReleaseCast();
        else if (AimReplaced && e.IsActionPressed("aim")) PressAlt();
        else if (AimReplaced && e.IsActionReleased("aim")) ReleaseCast();
    }

    private void PressAlt()
    {
        switch (ActiveSpell.AltMode)
        {
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
        if (spell.RequiresGround && !_player.IsOnFloor()) return;   // has to be cast standing

        SpellContext ctx = BuildContext(spell, alt);
        if (spell.RequiresTarget && ctx.Target == null) return;   // nothing under the crosshair
        if (spell.RequiresSnap && !ctx.Snapped) return;           // not on an anchor
        if (!spell.IsTargetValid(ctx)) return;   // validated here, on the machine that can see the target

        if (spell.Mode == CastMode.MultiPoint)
        {
            if (!_player.IsAiming) return;   // picks are made while aiming

            Node3D snapped = ctx.Snapped ? ctx.AimCollider as Node3D : null;
            if (snapped != null && _pickTargets.Contains(snapped)) return;   // can't pick the same anchor twice

            _points.Add(ctx.AimPoint);
            _pickTargets.Add(snapped);
            if (_points.Count < Mathf.Max(1, spell.PointCount)) return;   // keep picking

            ctx.Points = _points.ToArray();
            ctx.PointTargets = _pickTargets.ToArray();
            ClearPicks();
        }

        int castId = _nextCastId++;
        int index = ActiveIndex;
        // Nodes can't be sent, but their paths can: the same path finds the same node on every machine
        NodePath target = ctx.Target != null ? ctx.Target.GetPath() : new NodePath("");
        string[] pickPaths = new string[ctx.PointTargets.Length];
        for (int i = 0; i < pickPaths.Length; i++)
            pickPaths[i] = ctx.PointTargets[i] != null ? ctx.PointTargets[i].GetPath().ToString() : "";

        Rpc(MethodName.ReplicateBegin, index, castId,
            ctx.CastOrigin, ctx.AimPoint, ctx.AimNormal, ctx.AimDirection, ctx.AimHit, ctx.Points, pickPaths, alt, target);

        if (spell.Mode == CastMode.Hold || spell.Mode == CastMode.Channel) _heldCastId = castId;
        if (spell.Mode != CastMode.Hold) Spend(index);   // Hold pays when released
    }

    private void ReleaseCast()
    {
        if (_heldCastId < 0) return;

        int castId = _heldCastId;
        _heldCastId = -1;
        if (!_active.TryGetValue(castId, out SpellContext ctx)) return;

        // Let go before it's fully charged: cancelled, and it costs nothing
        if (ctx.Spell.RequiresFullCharge && ctx.ChargeFraction < 1f)
        {
            Rpc(MethodName.ReplicateCancel, castId);
            return;
        }
        if (ctx.Spell.Mode == CastMode.Hold) Spend(ctx.SpellIndex);
        Rpc(MethodName.ReplicateRelease, castId, ctx.HeldTime, ctx.AimPoint, ctx.AimNormal, ctx.AimHit);
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
    // For hold spells that fire on their own (Expulsion reaching full range): same as letting go
    public void ForceRelease()
    {
        if (IsLocal) ReleaseCast();
    }

    // ---------- Casting (every machine) ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateBegin(int spellIndex, int castId, Vector3 origin, Vector3 aimPoint,
                                Vector3 aimNormal, Vector3 aimDirection, bool aimHit, Vector3[] points,
                                string[] pickPaths, bool alt, NodePath targetPath)
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
            PointTargets = new Node3D[pickPaths.Length],
            IsAlt = alt,
            // Targets are sent as node paths: the same path finds the same entity on every machine
            Target = targetPath.IsEmpty ? null : GetNodeOrNull<Node3D>(targetPath),
        };
        if (!targetPath.IsEmpty && ctx.Target == null) return;   // the target is already gone here
        if (Multiplayer.IsServer()) Noise.Emit(this, origin, spell.NoiseRadius, _player);

        for (int i = 0; i < pickPaths.Length; i++)
        {
            if (pickPaths[i] == "") continue;
            ctx.PointTargets[i] = GetNodeOrNull<Node3D>(pickPaths[i]);
            if (ctx.PointTargets[i] == null) return;   // an anchor it was bound to is already gone here
        }

        if (spell.Mode == CastMode.Duration) ctx.Remaining = ctx.Stat(nameof(SpellDefinition.Duration));
        if (spell.Mode == CastMode.Delayed) ctx.Remaining = ctx.Stat(nameof(SpellDefinition.Delay));

        if (!spell.Begin(ctx)) return;

        bool runs = spell.Mode != CastMode.Instant && spell.Mode != CastMode.MultiPoint;
        if (runs) _active[castId] = ctx;
    }

    // Host only: tell every machine (itself included) which bodies a chain hit, in order
    public void BroadcastChain(SpellContext ctx, List<Node3D> hops)
    {
        if (!Multiplayer.IsServer() || hops.Count == 0) return;

        var paths = new string[hops.Count];
        for (int i = 0; i < hops.Count; i++) paths[i] = hops[i].GetPath().ToString();
        Rpc(MethodName.ReceiveChain, ctx.SpellIndex, ctx.CastOrigin, paths);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveChain(int spellIndex, Vector3 origin, string[] paths)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host routes chains
        if (spellIndex < 0 || spellIndex >= Spells.Count) return;

        var hops = new List<Node3D>();
        foreach (string path in paths)
            if (GetNodeOrNull<Node3D>(path) is Node3D hop) hops.Add(hop);

        var ctx = new SpellContext
        {
            Caster = _player,
            Spell = Spells[spellIndex],
            Mods = _mods,
            SpellIndex = spellIndex,
            CastOrigin = origin,
            Authoritative = Multiplayer.IsServer(),
        };
        Spells[spellIndex].ShowChain(ctx, hops.ToArray());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateRelease(int castId, float heldTime, Vector3 aimPoint, Vector3 aimNormal, bool aimHit)
    {
        if (!_active.Remove(castId, out SpellContext ctx)) return;
        ctx.HeldTime = heldTime;   // the owner's held time, so a charge-up resolves the same everywhere
        ctx.AimPoint = aimPoint;   // and the owner's aim at release, for spells that track a spot while held
        ctx.AimNormal = aimNormal;
        ctx.AimHit = aimHit;
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