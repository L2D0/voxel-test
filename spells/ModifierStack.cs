using Godot;
using System.Collections.Generic;

public partial class ModifierStack : Node
{
    [Signal] public delegate void ChangedEventHandler();

    // Toggle with the debug_modifiers action to test the system
    [Export] public Godot.Collections.Array<StatModifier> DebugModifiers = new();

    private readonly List<(StatModifier Mod, object Source)> _entries = new();

    public void Add(StatModifier mod, object source)
    {
        _entries.Add((mod, source));
        EmitSignal(SignalName.Changed);
    }

    public void RemoveSource(object source)
    {
        _entries.RemoveAll(e => e.Source == source);
        EmitSignal(SignalName.Changed);
    }

    public bool HasSource(object source) => _entries.Exists(e => e.Source == source);

    public float Get(SpellDefinition spell, string stat)
    {
        Variant raw = spell.Get(stat);
        if (raw.VariantType == Variant.Type.Nil)
        {
            GD.PushWarning($"{spell.SpellName} has no stat called '{stat}'");
            return 0f;
        }

        return Combine(raw.AsSingle(), mod => mod.AppliesTo(spell, stat));
    }

    // A player stat (max health, move speed...). The base comes from whatever owns the stat;
    // only Scope = Player modifiers with a matching name apply.
    public float GetPlayer(string stat, float baseValue) =>
        Combine(baseValue, mod => mod.Scope == ModScope.Player && mod.Stat == stat);

    // effective = (base + all Flat) * (1 + all Percent) * every Multiply
    private float Combine(float baseValue, System.Func<StatModifier, bool> applies)
    {
        float flat = 0f, percent = 0f, multiplier = 1f;
        foreach (var (mod, _) in _entries)
        {
            if (!applies(mod)) continue;

            switch (mod.Op)
            {
                case ModOp.Flat: flat += mod.Value; break;
                case ModOp.Percent: percent += mod.Value; break;
                case ModOp.Multiply: multiplier *= mod.Value; break;
            }
        }
        return (baseValue + flat) * (1f + percent) * multiplier;
    }

    // Debug panel: the test modifiers on the player, on or off
    public bool DebugOn => HasSource(this);

    public void ToggleDebug()
    {
        if (DebugOn) RemoveSource(this);
        else foreach (StatModifier mod in DebugModifiers) Add(mod, this);
    }
}