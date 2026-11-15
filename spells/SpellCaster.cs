using Godot;

public partial class SpellCaster : Node
{
    [Export] public Godot.Collections.Array<SpellDefinition> Spells = new();

    public int ActiveIndex { get; private set; }
    public SpellDefinition ActiveSpell => Spells.Count > 0 ? Spells[ActiveIndex] : null;

    private PlayerController _player;
    private Label _spellLabel;
    private float[] _charges;

    private SpellContext _holding;
    private int _holdingIndex = -1;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _spellLabel = GetNodeOrNull<Label>("%SpellLabel");

        _charges = new float[Spells.Count];
        for (int i = 0; i < Spells.Count; i++)
            _charges[i] = Spells[i].MaxCharges;
    }

    public float ChargesOf(int index) => _charges[index];

    public void SelectSpell(int index)
    {
        if (index < 0 || index >= Spells.Count || index == ActiveIndex) return;
        CancelHold();
        ActiveIndex = index;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Charges refill one at a time, continuously
        for (int i = 0; i < Spells.Count; i++)
            if (_charges[i] < Spells[i].MaxCharges)
                _charges[i] = Mathf.Min(_charges[i] + dt / Spells[i].RechargeTime, Spells[i].MaxCharges);

        if (_holding != null)
        {
            _holding.HeldTime += dt;
            Spells[_holdingIndex].Update(_holding, dt);
        }

        if (_spellLabel != null && ActiveSpell != null)
            _spellLabel.Text = $"{ActiveSpell.SpellName}  {_charges[ActiveIndex]:0.0} / {ActiveSpell.MaxCharges}";
    }

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

        SpellContext ctx = _player.BuildSpellContext();

        if (spell.Mode == CastMode.Instant)
        {
            _charges[ActiveIndex] -= 1f;
            spell.Begin(ctx);
        }
        else
        {
            _holding = ctx;
            _holdingIndex = ActiveIndex;
            spell.Begin(ctx);
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