using Godot;

public enum WeightClass { Light, Medium, Heavy, Boss }

// Optional facts about an entity that spells check: how heavy it is, and whether it's someone's summon.
// Add it as a child node named "EntityInfo". Anything without one counts as Medium and not a summon.
[GlobalClass]
public partial class EntityInfo : Node
{
    [Export] public WeightClass Weight = WeightClass.Medium;
    [Export] public bool IsSummon = false;

    public Node Summoner;   // set in code when something is summoned

    public static EntityInfo Of(Node node) => node?.GetNodeOrNull<EntityInfo>("EntityInfo");

    public static WeightClass WeightOf(Node node) => Of(node)?.Weight ?? WeightClass.Medium;

    public static bool IsSummoned(Node node) => Of(node)?.IsSummon ?? false;

    // How much of a knockback actually moves them. Pulls, Bubble lifts and ragdolls should check WeightOf too.
    public static float KnockbackScale(Node node) => WeightOf(node) switch
    {
        WeightClass.Light => 1.4f,
        WeightClass.Heavy => 0.4f,
        WeightClass.Boss => 0f,
        _ => 1f,
    };
}
