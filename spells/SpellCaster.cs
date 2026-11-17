using Godot;
using System.Collections.Generic;

public partial class SpellCaster : Node
{
    [Export] public Godot.Collections.Array<SpellDefinition> Spells = new();

    public int ActiveIndex { get; private set; }
    public SpellDefinition ActiveSpell => Spells.Count > 0 ? Spells[ActiveIndex] : null;
    public bool IsHolding => _holdingCastId >= 0;

    private PlayerController _player;
    private ModifierStack _mods;
    private Mana _mana;
    private Health _health;

    public event System.Action Casted;   // fired when a cast is paid for; the HUD listens
    private float[] _charges;

    // Owner only: the hold-cast this machine's player is currently performing
    private int _holdingCastId = -1;
    private int _holdingIndex = -1;
    private int _nextCastId;

    // Every machine: hold-casts in progress, by cast id
    private readonly Dictionary<int, SpellContext> _activeHolds = new();

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

    // Health costs are always payable: paying more HP than you have kills you
    private bool CanPay(SpellDefinition spell) =>
        spell.PaysWith == CostType.Health || _mana == null || _mana.CanAfford(CostOf(spell));

    private void Pay(SpellDefinition spell)
    {
        float cost = CostOf(spell);
        if (spell.PaysWith == CostType.Health) _health?.PayCost(cost);
        else _mana?.Spend(cost);
        Casted?.Invoke();
    }

    private SpellContext BuildContext(SpellDefinition spell)
    {
        SpellContext ctx = _player.BuildSpellContext();
        ctx.Spell = spell;
        ctx.Mods = _mods;
        return ctx;
    }

    // ---------- Selection ----------

    public void SelectSpell(int index)
    {
        if (index < 0 || index >= Spells.Count || index == ActiveIndex) return;
        CancelHold();
        ActiveIndex = index;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Every machine keeps hold-casts ticking
        foreach (SpellContext ctx in _activeHolds.Values)
        {
            ctx.HeldTime += dt;
            ctx.Spell.Update(ctx, dt);
        }

        if (!IsLocal) return;   // charges, preview and HUD are only for this machine's own player

        for (int i = 0; i < Spells.Count; i++)
        {
            int max = MaxChargesOf(i);
            if (_charges[i] > max) _charges[i] = max;
            else if (_charges[i] < max) _charges[i] = Mathf.Min(_charges[i] + dt / RechargeTimeOf(i), max);
        }

        UpdatePreview();
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

        bool show = spell != null && _holdingCastId < 0 && _player.IsAiming && !_player.MenuOpen;
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

        if (e.IsActionPressed("cast")) StartCast();
        else if (e.IsActionReleased("cast")) ReleaseCast();
    }

    private void StartCast()
    {
        SpellDefinition spell = ActiveSpell;
        if (spell == null || _holdingCastId >= 0 || _charges[ActiveIndex] < 1f || !CanPay(spell)) return;

        SpellContext ctx = BuildContext(spell);
        if (!spell.IsTargetValid(ctx)) return;   // validated here, on the machine that can see the target

        int castId = _nextCastId++;
        Rpc(MethodName.ReplicateBegin, ActiveIndex, castId,
            ctx.CastOrigin, ctx.AimPoint, ctx.AimNormal, ctx.AimDirection, ctx.AimHit);

        if (spell.Mode == CastMode.Instant)
        {
            _charges[ActiveIndex] -= 1f; Pay(spell);
        }
        else
        {
            _holdingCastId = castId;
            _holdingIndex = ActiveIndex;
        }
    }

    private void ReleaseCast()
    {
        if (_holdingCastId < 0) return;

        _charges[_holdingIndex] -= 1f; Pay(Spells[_holdingIndex]);
        Rpc(MethodName.ReplicateRelease, _holdingCastId);
        _holdingCastId = -1;
        _holdingIndex = -1;
    }

    private void CancelHold()
    {
        if (_holdingCastId < 0) return;

        Rpc(MethodName.ReplicateCancel, _holdingCastId);
        _holdingCastId = -1;
        _holdingIndex = -1;
    }

    // ---------- Casting (every machine) ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateBegin(int spellIndex, int castId, Vector3 origin, Vector3 aimPoint,
                                Vector3 aimNormal, Vector3 aimDirection, bool aimHit)
    {
        if (spellIndex < 0 || spellIndex >= Spells.Count) return;
        SpellDefinition spell = Spells[spellIndex];

        var ctx = new SpellContext
        {
            Caster = _player,
            Spell = spell,
            Mods = _mods,
            CastId = castId,
            Authoritative = Multiplayer.IsServer(),
            CastOrigin = origin,
            AimPoint = aimPoint,
            AimNormal = aimNormal,
            AimDirection = aimDirection,
            AimHit = aimHit,
        };

        if (!spell.Begin(ctx)) return;
        if (spell.Mode == CastMode.Hold) _activeHolds[castId] = ctx;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateRelease(int castId)
    {
        if (_activeHolds.Remove(castId, out SpellContext ctx)) ctx.Spell.Release(ctx);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReplicateCancel(int castId)
    {
        if (_activeHolds.Remove(castId, out SpellContext ctx)) ctx.Spell.Cancel(ctx);
    }
}