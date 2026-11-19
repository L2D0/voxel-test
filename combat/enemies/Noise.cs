using Godot;

// Sounds enemies can hear. Each has a radius; anything inside it may turn to look or come
// and check. Host only, since only the host runs enemy brains.
public static class Noise
{
    public static void Emit(Node from, Vector3 at, float radius, Node3D source)
    {
        if (radius <= 0f || from == null || !from.Multiplayer.IsServer()) return;

        foreach (Node node in from.GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy enemy && enemy != source) enemy.Hear(at, radius, source);
    }
}
