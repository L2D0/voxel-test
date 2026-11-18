using Godot;

public enum ModOp { Flat, Percent, Multiply }
public enum ModScope { AllSpells, Element, Spell, Player }   // Player: Stat is one of the PlayerStat names

[GlobalClass]
public partial class StatModifier : Resource
{
    [Export] public ModScope Scope = ModScope.Spell;
    [Export] public Element Element = Element.None;   // used when Scope = Element
    [Export] public SpellDefinition Spell;            // used when Scope = Spell
    [Export] public string Stat = "";                 // a spell's property name ("Radius"), or a PlayerStat name
    [Export] public ModOp Op = ModOp.Flat;
    [Export] public float Value = 0f;                 // Percent: 0.2 = +20%. Multiply: 1.5 = x1.5

    public bool AppliesTo(SpellDefinition spell, string stat)
    {
        if (stat != Stat) return false;

        return Scope switch
        {
            ModScope.AllSpells => true,
            ModScope.Element => spell.Element == Element,
            ModScope.Spell => Spell != null && Spell.ResourcePath == spell.ResourcePath,
            _ => false,
        };
    }
}