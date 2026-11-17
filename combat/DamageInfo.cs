using Godot;

public enum DamageType { Physical, Pure, Fire, Toxic, Dark, Shock, Wind, Water, Nature, Life, Crystal, Earth }

// One hit of damage, built by whatever dealt it and resolved by the target's Health
public class DamageInfo
{
    public float Amount;
    public DamageType Type = DamageType.Physical;
    public Node Source;
    public bool Crit;
    public float CritMultiplier = 2f;
    public bool CanKill = true;        // false: stops at 1 HP (Poison, Leach)

    public static DamageInfo FromSpell(SpellContext ctx, float amount) => new()
    {
        Amount = amount,
        Type = TypeOf(ctx.Spell.Element),
        Source = ctx.Caster,
    };

    public static DamageType TypeOf(Element element) => element switch
    {
        Element.Fire => DamageType.Fire,
        Element.Toxic => DamageType.Toxic,
        Element.Dark => DamageType.Dark,
        Element.Shock => DamageType.Shock,
        Element.Wind => DamageType.Wind,
        Element.Water => DamageType.Water,
        Element.Nature => DamageType.Nature,
        Element.Life => DamageType.Life,
        Element.Crystal => DamageType.Crystal,
        Element.Earth => DamageType.Earth,
        _ => DamageType.Physical,
    };
}