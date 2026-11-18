// Names of the player stats that StatModifiers with Scope = Player can change.
// Type these exactly into a modifier's Stat field. Base values live on the node that uses them
// (Health.MaxHealth, Mana.MaxMana, PlayerController's exports); modifiers adjust them from there.
public static class PlayerStat
{
    // Health and mana
    public const string MaxHealth = "MaxHealth";
    public const string MaxMana = "MaxMana";
    public const string ManaRegen = "ManaRegen";             // per second
    public const string ManaRegenDelay = "ManaRegenDelay";   // seconds after spending

    // Movement
    public const string MoveSpeed = "MoveSpeed";             // top ground speed
    public const string AirSpeed = "AirSpeed";               // how fast strafing can build speed in the air
    public const string JumpVelocity = "JumpVelocity";

    // Falling
    public const string SafeFallSpeed = "SafeFallSpeed";     // raise it to survive bigger drops
    public const string FallDamage = "FallDamage";           // multiplier on fall damage taken (Percent -0.5 = half)

    // Casting
    public const string RechargeRate = "RechargeRate";       // how fast every spell's charges refill (Percent 0.2 = 20% faster)

    // Resistances: "ResistFire", "ResistPhysical" and so on. Flat adds to the resistance (0.15 = 15%).
    public static string Resist(DamageType type) => "Resist" + type;
}
