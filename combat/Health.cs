using Godot;

[GlobalClass]
public partial class Health : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, Node source);
    [Signal] public delegate void DiedEventHandler(Node source);
    [Signal] public delegate void ChangedEventHandler(float current, float max);

    [Export] public float MaxHealth = 100f;

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    public override void _Ready() => Current = MaxHealth;

    public void TakeDamage(float amount, Node source = null)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Max(Current - amount, 0f);
        EmitSignal(SignalName.Damaged, amount, source);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
        if (IsDead) EmitSignal(SignalName.Died, source);
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(Current + amount, MaxHealth);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
    }

    public void Reset()
    {
        Current = MaxHealth;
        EmitSignal(SignalName.Changed, Current, MaxHealth);
    }

    // Finds the Health component on any entity, or null if it can't be damaged
    public static Health Of(Node node) => node?.GetNodeOrNull<Health>("Health");
}