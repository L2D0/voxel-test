using Godot;

public partial class SpellCaster : Node
{
    [Export] public Godot.Collections.Array<SpellDefinition> Spells = new();

    public int ActiveIndex { get; private set; }
    public SpellDefinition ActiveSpell => Spells.Count > 0 ? Spells[ActiveIndex] : null;

    private PlayerController _player;
    private ModifierStack _mods;
    private Label _spellLabel;
    private float[] _charges;

    private SpellContext _holding;
    private int _holdingIndex = -1;

    private Node3D _preview;
    private SpellDefinition _previewSpell;
    private MeshInstance3D _rangeBubble;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _mods = _player.GetNode<ModifierStack>("Modifiers");
        _spellLabel = GetNodeOrNull<Label>("%SpellLabel");

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

    // ---------- Stats ----------

    public float Stat(SpellDefinition spell, string stat) => _mods.Get(spell, stat);

    public int MaxChargesOf(int index) =>
        Mathf.Max(1, Mathf.RoundToInt(Stat(Spells[index], nameof(SpellDefinition.MaxCharges))));

    private float RechargeTimeOf(int index) =>
        Mathf.Max(0.05f, Stat(Spells[index], nameof(SpellDefinition.RechargeTime)));

    public float ChargesOf(int index) => _charges[index];

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

        for (int i = 0; i < Spells.Count; i++)
        {
            int max = MaxChargesOf(i);
            if (_charges[i] > max) _charges[i] = max;   // a modifier was removed
            else if (_charges[i] < max) _charges[i] = Mathf.Min(_charges[i] + dt / RechargeTimeOf(i), max);
        }

        if (_holding != null)
        {
            _holding.HeldTime += dt;
            Spells[_holdingIndex].Update(_holding, dt);
        }

        UpdatePreview();

        if (_spellLabel != null && ActiveSpell != null)
            _spellLabel.Text = $"{ActiveSpell.SpellName}  {_charges[ActiveIndex]:0.0} / {MaxChargesOf(ActiveIndex)}";
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

        bool show = spell != null && _holding == null && _player.IsAiming && !_player.MenuOpen;
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

    // ---------- Casting ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (_player.MenuOpen) return;

        if (e.IsActionPressed("cast")) StartCast();
        else if (e.IsActionReleased("cast")) ReleaseCast();
    }

    private void StartCast()
    {
        SpellDefinition spell = ActiveSpell;
        if (spell == null || _holding != null || _charges[ActiveIndex] < 1f) return;

        SpellContext ctx = BuildContext(spell);
        if (!spell.Begin(ctx)) return;   // invalid target, nothing spent

        if (spell.Mode == CastMode.Instant)
        {
            _charges[ActiveIndex] -= 1f;
        }
        else
        {
            _holding = ctx;
            _holdingIndex = ActiveIndex;
        }
    }

    private void ReleaseCast()
    {
        if (_holding == null) return;

        _charges[_holdingIndex] -= 1f;
        Spells[_holdingIndex].Release(_holding);
        _holding = null;
        _holdingIndex = -1;
    }

    private void CancelHold()
    {
        if (_holding == null) return;

        Spells[_holdingIndex].Cancel(_holding);
        _holding = null;
        _holdingIndex = -1;
    }
}