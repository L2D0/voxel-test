using Godot;

// The caster's mana. Owned by the player's own machine, like movement and spell charges,
// so casting never waits on the network. Mana granted by others is sent to the owner.
[GlobalClass]
public partial class Mana : Node
{
    [Signal] public delegate void ChangedEventHandler(float current, float max);

    [Export] public float MaxMana = 100f;
    [Export] public float RegenPerSecond = 8f;
    [Export] public float RegenDelay = 1.5f;   // seconds after spending before regen starts again

    public float Current { get; private set; }

    private float _delay;

    private bool IsOwner => GetParent().IsMultiplayerAuthority();

    public override void _Ready() => Current = MaxMana;

    public bool CanAfford(float amount) => Current >= amount;

    public bool Spend(float amount)
    {
        if (amount <= 0f) return true;
        if (Current < amount) return false;

        Current -= amount;
        _delay = RegenDelay;
        EmitSignal(SignalName.Changed, Current, MaxMana);
        return true;
    }

    // Channel upkeep: takes what it can, returns false once there's nothing left
    public bool Drain(float amount)
    {
        if (amount <= 0f) return true;
        _delay = RegenDelay;
        if (Current <= 0f) return false;

        Current = Mathf.Max(Current - amount, 0f);
        EmitSignal(SignalName.Changed, Current, MaxMana);
        return Current > 0f;
    }

    // Callable from any machine; the owner applies it
    public void Restore(float amount)
    {
        if (amount <= 0f) return;
        if (IsOwner) Add(amount);
        else RpcId(GetParent().GetMultiplayerAuthority(), MethodName.ReceiveRestore, amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRestore(float amount)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host grants mana
        Add(amount);
    }

    private void Add(float amount)
    {
        float before = Current;
        Current = Mathf.Min(Current + amount, MaxMana);
        if (Current != before) EmitSignal(SignalName.Changed, Current, MaxMana);
    }

    public override void _Process(double delta)
    {
        if (!IsOwner || Current >= MaxMana) return;

        float dt = (float)delta;
        if (_delay > 0f)
        {
            _delay -= dt;
            return;
        }
        Add(RegenPerSecond * dt);
    }
}