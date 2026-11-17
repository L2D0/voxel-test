using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Health : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, Node source);
    [Signal] public delegate void DiedEventHandler(Node source);
    [Signal] public delegate void ChangedEventHandler(float current, float max);

    public const float MaxResistance = 0.75f;

    [Export] public float MaxHealth = 100f;
    [Export] public Resistances Resistances;   // optional: base resistances for this kind of entity

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    // Resistance from gear and buffs, per instance, tagged with what granted it
    private readonly List<(object Source, DamageType Type, float Amount)> _bonusResistances = new();

    public override void _Ready() => Current = MaxHealth;

    // ---------- Resistances ----------

    public void AddResistance(object source, DamageType type, float amount) =>
        _bonusResistances.Add((source, type, amount));

    public void RemoveResistances(object source) =>
        _bonusResistances.RemoveAll(r => r.Source == source);

    // Additive, capped at 75%; vulnerability (negative) has no floor
    public float ResistanceTo(DamageType type)
    {
        if (type == DamageType.Pure) return 0f;

        float total = Resistances?.Get(type) ?? 0f;
        foreach (var r in _bonusResistances)
            if (r.Type == type) total += r.Amount;

        return Mathf.Min(total, MaxResistance);
    }

    // ---------- Damage ----------

    // The pipeline. Every hit goes through these steps in this order.
    public float Resolve(DamageInfo hit)
    {
        float amount = hit.Amount;                    // 1. base, already including spell modifiers
        if (hit.Crit) amount *= hit.CritMultiplier;   // 2. weak point
                                                      // 3. status amplification: added with statuses
        amount *= 1f - ResistanceTo(hit.Type);        // 4. resistance or vulnerability
        return Mathf.Max(amount, 0f);
    }

    // Only the host changes health; everyone else is told the result
    public void TakeDamage(DamageInfo hit)
    {
        if (!Multiplayer.IsServer() || IsDead || hit == null) return;

        float amount = Resolve(hit);
        float floor = hit.CanKill ? 0f : Mathf.Min(1f, Current);   // non-lethal damage stops at 1 HP
        float current = Mathf.Max(Current - amount, floor);
        float dealt = Current - current;
        if (dealt <= 0f) return;

        ApplyState(current, dealt, hit.Source);
        Rpc(MethodName.ReceiveState, current, dealt);
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