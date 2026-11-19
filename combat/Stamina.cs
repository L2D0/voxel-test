using Godot;

// The player's stamina. Like movement, only the player's own machine tracks it: nothing else needs it.
// Sprinting drains it. Run it dry and you can't sprint again until it's back to RecoverFraction.
public partial class Stamina : Node
{
    [Export] public float MaxStamina = 100f;
    [Export] public float RegenPerSecond = 25f;
    [Export] public float RegenDelay = 1f;                                   // seconds after draining before it refills
    [Export(PropertyHint.Range, "0,1")] public float RecoverFraction = 0.3f; // run dry: locked out until this full

    public float Current { get; private set; }
    public bool Exhausted { get; private set; }
    public float Max => Stat(PlayerStat.MaxStamina, MaxStamina);
    public float Regen => Stat(PlayerStat.StaminaRegen, RegenPerSecond);
    public bool CanSprint => !Exhausted && Current > 0f;

    private ModifierStack _mods;
    private float _delay;

    private float Stat(string stat, float baseValue) => _mods != null ? _mods.GetPlayer(stat, baseValue) : baseValue;

    public override void _Ready()
    {
        _mods = GetParent().GetNodeOrNull<ModifierStack>("Modifiers");
        Current = Max;
    }

    public void Drain(float amount)
    {
        Current = Mathf.Max(Current - amount, 0f);
        _delay = RegenDelay;
        if (Current <= 0f) Exhausted = true;
    }

    public void Restore(float amount) => Current = Mathf.Min(Current + amount, Max);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Current = Mathf.Min(Current, Max);   // max can drop when gear comes off

        if (_delay > 0f)
        {
            _delay -= dt;
            return;
        }

        Current = Mathf.Min(Current + Regen * dt, Max);
        if (Exhausted && Current >= Max * RecoverFraction) Exhausted = false;
    }
}
