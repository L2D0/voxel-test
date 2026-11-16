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

        float flat = 0f, percent = 0f, multiplier = 1f;
        foreach (var (mod, _) in _entries)
        {
            if (!mod.AppliesTo(spell, stat)) continue;

            switch (mod.Op)
            {
                case ModOp.Flat: flat += mod.Value; break;
                case ModOp.Percent: percent += mod.Value; break;
                case ModOp.Multiply: multiplier *= mod.Value; break;
            }
        }

        return (raw.AsSingle() + flat) * (1f + percent) * multiplier;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("debug_modifiers")) return;

        if (HasSource(this))
        {
            RemoveSource(this);
            GD.Print("Debug modifiers OFF");
        }
        else
        {
            foreach (StatModifier mod in DebugModifiers) Add(mod, this);
            GD.Print("Debug modifiers ON");
        }
    }
}