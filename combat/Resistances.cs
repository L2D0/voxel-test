using Godot;

// Base resistances for a kind of entity. Shared between instances, so never changed at runtime.
// 0.25 = takes 25% less, -0.5 = takes 50% more
[GlobalClass]
public partial class Resistances : Resource
{
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Physical;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Fire;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Toxic;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Dark;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Shock;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Wind;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Water;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Nature;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Life;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Crystal;
    [Export(PropertyHint.Range, "-2,1,0.05")] public float Earth;

    public float Get(DamageType type) => type switch
    {
        DamageType.Physical => Physical,
        DamageType.Fire => Fire,
        DamageType.Toxic => Toxic,
        DamageType.Dark => Dark,
        DamageType.Shock => Shock,
        DamageType.Wind => Wind,
        DamageType.Water => Water,
        DamageType.Nature => Nature,
        DamageType.Life => Life,
        DamageType.Crystal => Crystal,
        DamageType.Earth => Earth,
        _ => 0f,   // Pure has no resistance
    };
}