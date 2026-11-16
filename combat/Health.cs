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

    // Only the host changes health; everyone else is told the result
    public void TakeDamage(float amount, Node source = null)
    {
        if (!Multiplayer.IsServer() || IsDead || amount <= 0f) return;

        float current = Mathf.Max(Current - amount, 0f);
        ApplyState(current, amount, source);
        Rpc(MethodName.ReceiveState, current, amount);
    }

    public void Heal(float amount)
    {
        if (!Multiplayer.IsServer() || IsDead || amount <= 0f) return;

        float current = Mathf.Min(Current + amount, MaxHealth);
        ApplyState(current, 0f, null);
        Rpc(MethodName.ReceiveState, current, 0f);
    }

    public void Reset()
    {
        if (!Multiplayer.IsServer()) return;

        ApplyState(MaxHealth, 0f, null);
        Rpc(MethodName.ReceiveState, MaxHealth, 0f);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveState(float current, float damage)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only accept the host's word
        ApplyState(current, damage, null);
    }

    private void ApplyState(float current, float damage, Node source)
    {
        bool wasDead = IsDead;
        Current = current;

        if (damage > 0f) EmitSignal(SignalName.Damaged, damage, source);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
        if (!wasDead && IsDead) EmitSignal(SignalName.Died, source);
    }

    public static Health Of(Node node) => node?.GetNodeOrNull<Health>("Health");
}