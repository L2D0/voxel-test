using Godot;

// Players and their summons are on one side, everything else is on the other.
// Friendly fire lets players hurt each other; it's a world rule the host sets.
public static class Teams
{
    // Only the host deals damage, but clients need it too so their targeting matches. Session syncs it.
    public static bool FriendlyFire;

    public static bool IsPlayer(Node node) => node != null && node.IsInGroup("players");

    // Which side something fights for. A summon fights for whoever summoned it.
    public static bool IsPlayerSide(Node node)
    {
        if (EntityInfo.Of(node) is EntityInfo info && info.IsSummon && info.Summoner != null && info.Summoner != node)
            return IsPlayerSide(info.Summoner);
        return IsPlayer(node);
    }

    // Whoever is ultimately in charge of it: a summon's summoner, otherwise itself
    public static Node OwnerOf(Node node)
    {
        if (EntityInfo.Of(node) is EntityInfo info && info.IsSummon && info.Summoner != null && info.Summoner != node)
            return OwnerOf(info.Summoner);
        return node;
    }

    public static bool AreAllies(Node a, Node b) => a == b || IsPlayerSide(a) == IsPlayerSide(b);

    // Can this hit hurt that body? Every damage source asks this.
    // Self-damage is never this function's call: each spell decides it (Heatburst's self-launch).
    public static bool CanHarm(Node attacker, Node victim)
    {
        if (attacker == null || victim == null) return true;   // environmental damage hurts everyone
        if (OwnerOf(attacker) == OwnerOf(victim)) return false;   // you and your own summons
        if (!AreAllies(attacker, victim)) return true;
        return FriendlyFire && IsPlayerSide(attacker) && IsPlayerSide(victim);
    }

    // Does this body pass a spell's target filter, from this caster's point of view?
    // With friendly fire on, other players count as enemies as well as allies.
    public static bool Matches(Node caster, Node body, TargetFilter filter)
    {
        TargetFilter side = AreAllies(caster, body) ? TargetFilter.Allies : TargetFilter.Enemies;
        if (side == TargetFilter.Allies && CanHarm(caster, body)) side |= TargetFilter.Enemies;
        return (filter & side) != 0;
    }
}
