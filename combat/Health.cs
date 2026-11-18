using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class Health : Node
{
    [Signal] public delegate void DamagedEventHandler(float amount, Node source);
    [Signal] public delegate void DiedEventHandler(Node source);
    [Signal] public delegate void RevivedEventHandler();
    [Signal] public delegate void ChangedEventHandler(float current, float max);

    public const float MaxResistance = 0.75f;

    [Export] public float MaxHealth = 100f;    // the base; Max is what's in effect after modifiers
    [Export] public Resistances Resistances;   // optional: base resistances for this kind of entity

    public float Current { get; private set; }
    public float Shield { get; private set; }   // total temporary HP across every shield
    public bool IsDead => Current <= 0f;

    // Max health right now. Players' modifiers (gear, nodes, buffs) can change it; everything else uses the base.
    public float Max => _mods != null ? _mods.GetPlayer(PlayerStat.MaxHealth, MaxHealth) : MaxHealth;

    private ModifierStack _mods;   // only players have one

    // Resistance from gear and buffs, per instance, tagged with what granted it
    private readonly List<(object Source, DamageType Type, float Amount)> _bonusResistances = new();

    // Host only. Newest last, and the newest takes hits first.
    private class ShieldLayer
    {
        public object Source;
        public float Amount;
        public float Remaining;   // 0 = no time limit
    }
    private readonly List<ShieldLayer> _shields = new();

    public override void _Ready()
    {
        _mods = GetParent().GetNodeOrNull<ModifierStack>("Modifiers");
        if (_mods != null) _mods.Changed += OnModifiersChanged;
        Current = Max;
    }

    // Max health went up or down. Raising it doesn't heal; lowering it below current HP trims HP down.
    private void OnModifiersChanged()
    {
        if (Multiplayer.IsServer() && Current > Max) Broadcast(Max, 0f, null);
        else EmitSignal(SignalName.Changed, Current, Max);   // the HUD redraws the bar at its new length
    }

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
        if (_mods != null) total += _mods.GetPlayer(PlayerStat.Resist(type), 0f);   // gear and nodes
        foreach (var r in _bonusResistances)
            if (r.Type == type) total += r.Amount;

        return Mathf.Min(total, MaxResistance);
    }

    // ---------- Shields ----------

    public void AddShield(object source, float amount, float duration = 0f)
    {
        if (!Multiplayer.IsServer() || IsDead || amount <= 0f) return;

        _shields.Add(new ShieldLayer { Source = source, Amount = amount, Remaining = duration });
        Broadcast(Current, 0f, null);
    }

    public void RemoveShields(object source)
    {
        if (!Multiplayer.IsServer()) return;
        if (_shields.RemoveAll(s => s.Source == source) > 0) Broadcast(Current, 0f, null);
    }

    // Returns whatever the shields couldn't soak
    private float Absorb(float amount)
    {
        for (int i = _shields.Count - 1; i >= 0 && amount > 0f; i--)
        {
            ShieldLayer layer = _shields[i];
            float taken = Mathf.Min(layer.Amount, amount);
            layer.Amount -= taken;
            amount -= taken;
            if (layer.Amount <= 0f) _shields.RemoveAt(i);
        }
        return amount;
    }

    private float TotalShield()
    {
        float total = 0f;
        foreach (ShieldLayer layer in _shields) total += layer.Amount;
        return total;
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer() || _shields.Count == 0) return;

        float dt = (float)delta;
        bool expired = false;
        for (int i = _shields.Count - 1; i >= 0; i--)
        {
            ShieldLayer layer = _shields[i];
            if (layer.Remaining <= 0f) continue;   // no time limit

            layer.Remaining -= dt;
            if (layer.Remaining <= 0f)
            {
                _shields.RemoveAt(i);
                expired = true;
            }
        }
        if (expired) Broadcast(Current, 0f, null);
    }

    // ---------- Damage ----------

    // The pipeline. Every hit goes through these steps in this order.
    public float Resolve(DamageInfo hit)
    {
        float amount = hit.Amount;                    // 1. base, already including spell modifiers
        if (hit.Crit) amount *= hit.CritMultiplier;   // 2. weak point
        if (hit.Type != DamageType.Pure)              // 3. status amplification (Wet into Shock and so on)
            amount *= 1f + (StatusEffects.Of(GetParent())?.AmplificationFor(hit.Type) ?? 0f);
        amount *= 1f - ResistanceTo(hit.Type);        // 4. resistance or vulnerability
        return Mathf.Max(amount, 0f);
    }

    // Only the host changes health; everyone else is told the result
    public void TakeDamage(DamageInfo hit)
    {
        if (!Multiplayer.IsServer() || IsDead || hit == null) return;

        float amount = Resolve(hit);
        float toHealth = hit.Type == DamageType.Pure ? amount : Absorb(amount);   // 5. shields
        float absorbed = amount - toHealth;

        float floor = hit.CanKill ? 0f : Mathf.Min(1f, Current);   // non-lethal damage stops at 1 HP
        float current = Mathf.Max(Current - toHealth, floor);
        float dealt = Current - current;
        if (dealt <= 0f && absorbed <= 0f) return;

        Broadcast(current, dealt + absorbed, hit.Source);
    }

    // Damage a player's own machine puts on its own player: HP costs (Pure) and fall damage (Physical).
    // The owner asks the host, and the host checks it really is the owner asking.
    public void TakeOwnDamage(float amount, DamageType type)
    {
        if (amount <= 0f) return;
        if (Multiplayer.IsServer()) ApplyOwnDamage(amount, type);
        else RpcId(1, MethodName.RequestOwnDamage, amount, (int)type);
    }

    // HP spent on casting: Pure, so it skips resistances and shields, and it can kill
    public void PayCost(float amount) => TakeOwnDamage(amount, DamageType.Pure);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestOwnDamage(float amount, int type)
    {
        if (!Multiplayer.IsServer()) return;
        if (Multiplayer.GetRemoteSenderId() != GetParent().GetMultiplayerAuthority()) return;   // only for yourself
        ApplyOwnDamage(amount, (DamageType)type);
    }

    private void ApplyOwnDamage(float amount, DamageType type) =>
        TakeDamage(new DamageInfo { Amount = amount, Type = type, Source = GetParent() });

    public void Heal(float amount)
    {
        if (!Multiplayer.IsServer() || IsDead || amount <= 0f) return;
        Broadcast(Mathf.Min(Current + amount, Max), 0f, null);
    }

    public void Reset()
    {
        if (!Multiplayer.IsServer()) return;
        _shields.Clear();
        Broadcast(Max, 0f, null);
    }

    // Bring someone back while they're down (Life's Revive). Host only.
    public void Revive(float fraction = 1f)
    {
        if (!Multiplayer.IsServer() || !IsDead) return;
        Broadcast(Mathf.Max(Max * fraction, 1f), 0f, null);
    }

    // A dead player's machine asking to come back once its respawn timer runs out
    public void RequestRespawn()
    {
        if (Multiplayer.IsServer()) RespawnIfDead();
        else RpcId(1, MethodName.ReceiveRespawnRequest);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRespawnRequest()
    {
        if (!Multiplayer.IsServer()) return;
        if (Multiplayer.GetRemoteSenderId() != GetParent().GetMultiplayerAuthority()) return;   // only for yourself
        RespawnIfDead();
    }

    private void RespawnIfDead()
    {
        if (IsDead) Reset();   // already revived by someone? then there's nothing to do
    }

    // ---------- Sync ----------

    private void Broadcast(float current, float damage, Node source)
    {
        float shield = TotalShield();
        ApplyState(current, shield, damage, source);
        Rpc(MethodName.ReceiveState, current, shield, damage);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveState(float current, float shield, float damage)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only accept the host's word
        ApplyState(current, shield, damage, null);
    }

    private void ApplyState(float current, float shield, float damage, Node source)
    {
        bool wasDead = IsDead;
        Current = current;
        Shield = shield;

        if (damage > 0f) EmitSignal(SignalName.Damaged, damage, source);
        EmitSignal(SignalName.Changed, Current, Max);
        if (!wasDead && IsDead) EmitSignal(SignalName.Died, source); 
        if (wasDead && !IsDead) EmitSignal(SignalName.Revived);
    }

    public static Health Of(Node node) => node?.GetNodeOrNull<Health>("Health");
}