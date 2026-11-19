using Godot;

// A spell an enemy can use, plus when the AI should use it. The spell itself is an ordinary
// SpellDefinition, so a bite, a fireball or a pillar works the same for enemies as for players.
[GlobalClass]
public partial class EnemyAbility : Resource
{
    [Export] public SpellDefinition Spell;
    [Export] public float MinRange = 0f;
    [Export] public float MaxRange = 2.2f;
    [Export] public float Cooldown = 2f;
    [Export] public float Windup = 0.5f;     // the telegraph before it lands: the window to dodge
    [Export] public float Recovery = 0.4f;   // stands still afterwards: the window to punish
    [Export] public float Weight = 1f;       // relative chance when several abilities are ready
    [Export] public bool NeedsSight = true;
    [Export(PropertyHint.Range, "0,1")] public float BelowHealth = 1f;   // only used under this fraction of HP; 1 = always
}
