using Godot;
using System.Collections.Generic;

// Works out the path of a chain: from the first target, hop to the nearest valid body
// that hasn't been hit yet, again and again. Run it on the host, then BroadcastChain the result.
public static class Chain
{
    public static List<Node3D> Route(Node caster, Node3D first, int extraHops, float range,
                                     TargetFilter filter, bool needsSight)
    {
        var hops = new List<Node3D> { first };
        var visited = new HashSet<Node3D> { first };
        Node3D current = first;

        for (int i = 0; i < extraHops; i++)
        {
            Node3D next = NearestFrom(caster, current, range, filter, needsSight, visited);
            if (next == null) break;   // nothing left in reach: the chain ends early

            hops.Add(next);
            visited.Add(next);
            current = next;
        }
        return hops;
    }

    private static Node3D NearestFrom(Node caster, Node3D from, float range, TargetFilter filter,
                                      bool needsSight, HashSet<Node3D> visited)
    {
        Vector3 origin = from.GlobalPosition;
        var space = from.GetWorld3D().DirectSpaceState;

        Node3D best = null;
        float bestDistance = range;
        foreach (Node3D body in SpellFx.BodiesInSphere(from, origin, range, SpellFx.DamageableMask))
        {
            if (visited.Contains(body) || body == caster || Health.Of(body) == null) continue;
            if (!Teams.Matches(caster, body, filter)) continue;

            float distance = origin.DistanceTo(body.GlobalPosition);
            if (distance >= bestDistance) continue;

            if (needsSight)
            {
                var sight = PhysicsRayQueryParameters3D.Create(origin, body.GlobalPosition, SpellFx.WorldLayer);
                if (space.IntersectRay(sight).Count > 0) continue;
            }

            best = body;
            bestDistance = distance;
        }
        return best;
    }
}
