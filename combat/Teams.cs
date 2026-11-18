using Godot;

// For now: players are allies of each other, everything else is an enemy.
// Friendly fire becomes a setting checked here later.
public static class Teams
{
    public static bool IsPlayer(Node node) => node != null && node.IsInGroup("players");

    public static bool AreAllies(Node a, Node b) => a == b || IsPlayer(a) == IsPlayer(b);
}
