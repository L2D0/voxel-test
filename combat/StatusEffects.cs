using Godot;
using System.Collections.Generic;

// Holds an entity's active statuses. The host applies, ticks and expires them; everyone else mirrors.
[GlobalClass]
public partial class StatusEffects : Node
{
    [Signal] public delegate void ChangedEventHandler();

    public class Instance
    {
        public StatusDefinition Def;
        public int Stacks;
        public float Remaining;
        public float TickTimer;
        public Node Source;
    }

    private readonly Dictionary<StatusDefinition, Instance> _active = new();
    private Health _health;

    public IEnumerable<Instance> Active => _active.Values;

    public static StatusEffects Of(Node node) => node?.GetNodeOrNull<StatusEffects>("Statuses");

    public override void _Ready()
    {
        _health = Health.Of(GetParent());
        if (_health != null) _health.Died += source => ClearAll();
    }

    public int StacksOf(StatusDefinition def) => _active.TryGetValue(def, out Instance inst) ? inst.Stacks : 0;

    // Step 3 of the damage pipeline: how much this target's statuses boost a damage type
    public float AmplificationFor(DamageType type)
    {
        float total = 0f;
        foreach (Instance inst in _active.Values)
            foreach (DamageAmp amp in inst.Def.Amplifies)
                if (amp != null && amp.Type == type) total += amp.PerStack * inst.Stacks;
        return total;
    }

    // ---------- Host ----------

    public void Apply(StatusDefinition def, Node source, int stacks = 1)
    {
        if (!Multiplayer.IsServer() || def == null) return;
        if (_health != null && _health.IsDead) return;

        if (!_active.TryGetValue(def, out Instance inst))
        {
            inst = new Instance { Def = def, TickTimer = def.TickInterval };
            _active[def] = inst;
        }

        inst.Stacks = Mathf.Min(inst.Stacks + stacks, def.MaxStacks);
        inst.Remaining = def.Duration;   // any new application resets the timer
        inst.Source = source;
        Broadcast(inst);
    }

    public void Remove(StatusDefinition def)
    {
        if (!Multiplayer.IsServer() || !_active.TryGetValue(def, out Instance inst)) return;

        inst.Stacks = 0;
        _active.Remove(def);
        Broadcast(inst);
    }

    // Cleanse(StatusCategory.Debuff) removes every debuff
    public void Cleanse(StatusCategory category)
    {
        foreach (StatusDefinition def in new List<StatusDefinition>(_active.Keys))
            if (def.Category == category) Remove(def);
    }

    public void ClearAll()
    {
        foreach (StatusDefinition def in new List<StatusDefinition>(_active.Keys))
            Remove(def);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool host = Multiplayer.IsServer();

        // Copy first: a tick can kill the target, which clears statuses mid-loop
        foreach (Instance inst in new List<Instance>(_active.Values))
        {
            inst.Remaining -= dt;   // everyone counts down, for display
            if (!host || !_active.ContainsKey(inst.Def)) continue;

            if (inst.Def.TickDamagePerStack > 0f)
            {
                inst.TickTimer -= dt;
                while (inst.TickTimer <= 0f && _active.ContainsKey(inst.Def))
                {
                    inst.TickTimer += inst.Def.TickInterval;
                    Tick(inst);
                }
            }

            if (inst.Remaining <= 0f && _active.ContainsKey(inst.Def)) Expire(inst);
        }
    }

    private void Tick(Instance inst)
    {
        _health?.TakeDamage(new DamageInfo
        {
            Amount = inst.Def.TickDamagePerStack * inst.Stacks,
            Type = inst.Def.TickType,
            Source = IsInstanceValid(inst.Source) ? inst.Source : null,   // the caster may have left
            CanKill = inst.Def.TickCanKill,
        });
    }

    private void Expire(Instance inst)
    {
        if (inst.Def.StackMode == StackMode.OneAtATime && inst.Stacks > 1)
        {
            inst.Stacks--;
            inst.Remaining = inst.Def.Duration;
            Broadcast(inst);
        }
        else Remove(inst.Def);
    }

    // ---------- Sync ----------

    private void Broadcast(Instance inst)
    {
        EmitSignal(SignalName.Changed);
        Rpc(MethodName.ReceiveStatus, inst.Def.ResourcePath, inst.Stacks, inst.Remaining);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveStatus(string path, int stacks, float remaining)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;

        var def = GD.Load<StatusDefinition>(path);
        if (def == null) return;

        if (stacks <= 0) _active.Remove(def);
        else
        {
            if (!_active.TryGetValue(def, out Instance inst))
                _active[def] = inst = new Instance { Def = def };
            inst.Stacks = stacks;
            inst.Remaining = remaining;
        }
        EmitSignal(SignalName.Changed);
    }
}