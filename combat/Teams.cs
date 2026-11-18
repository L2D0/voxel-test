using Godot;

// For now: players and their summons are on one side, everything else is on the other.
// Friendly fire becomes a setting checked here later.
public static class Teams
{
    public static bool IsPlayer(Node node) => node != null && node.IsInGroup("players");

    // Which side something fights for. A summon fights for whoever summoned it.
    public static bool IsPlayerSide(Node node)
    {
        if (EntityInfo.Of(node) is EntityInfo info && info.IsSummon && info.Summoner != null && info.Summoner != node)
            return IsPlayerSide(info.Summoner);
        return IsPlayer(node);
    }

    public static bool AreAllies(Node a, Node b) => a == b || IsPlayerSide(a) == IsPlayerSide(b);

    // Does this body pass a spell's target filter, from this caster's point of view?
    public static bool Matches(Node caster, Node body, TargetFilter filter)
    {
        TargetFilter needed = AreAllies(caster, body) ? TargetFilter.Allies : TargetFilter.Enemies;
        return (filter & needed) != 0;
    }
}