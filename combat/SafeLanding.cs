using Godot;

// Finds somewhere a body can stand. Every teleport goes through this (Electromotive rush,
// Shadow step, Guiding light, waystones), and so do spawning and respawning, so nothing gets
// dropped inside terrain, inside a pillar, on a cliff face, or out over the void.
public static class SafeLanding
{
    private const uint Solid = SpellFx.WorldLayer | SpellFx.StructureLayer;   // terrain and extrusions
    private const float ProbeAbove = 4f;     // teleports look for ground starting this far above the target...
    private const float ProbeBelow = 12f;    // ...down to this far below it
    private const float MinFloorNormal = 0.6f;   // steeper than about 53 degrees isn't standing ground
    private const int Rings = 4;             // if the exact spot fails, try rings around it
    private const float RingStep = 1f;       // metres between rings
    private const int PointsPerRing = 8;     // the first ring; each ring further out tries more

    // Teleports: where the centre of a capsule this size can go, as close to target as possible.
    // Null if nothing within a few metres works. exclude: the body being moved, so it doesn't block itself.
    public static Vector3? Find(Node3D context, Vector3 target, float radius, float height, Rid exclude = default)
    {
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        return Search(target, p => TryColumn(space, p + Vector3.Up * ProbeAbove, p + Vector3.Down * ProbeBelow,
                                             radius, height, exclude));
    }

    // Spawning: the highest standing ground under target's x and z, searching the whole height of the world.
    // Caves underneath don't matter; the first ground from the sky down wins.
    public static Vector3? FindSurface(Node3D context, Vector3 target, float top, float bottom,
                                       float radius, float height, Rid exclude = default)
    {
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;
        return Search(target, p => TryColumn(space, new Vector3(p.X, top, p.Z), new Vector3(p.X, bottom, p.Z),
                                             radius, height, exclude));
    }

    // The spot itself, then rings around it
    private static Vector3? Search(Vector3 target, System.Func<Vector3, Vector3?> tryAt)
    {
        if (tryAt(target) is Vector3 spot) return spot;

        for (int ring = 1; ring <= Rings; ring++)
        {
            int count = PointsPerRing * ring;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Tau * i / count;
                Vector3 offset = new(Mathf.Cos(angle) * ring * RingStep, 0f, Mathf.Sin(angle) * ring * RingStep);
                if (tryAt(target + offset) is Vector3 found) return found;
            }
        }
        return null;
    }

    // Casts one ray straight down. If it lands on standing ground and the capsule fits there, that's the spot.
    private static Vector3? TryColumn(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to,
                                      float radius, float height, Rid exclude)
    {
        var ray = PhysicsRayQueryParameters3D.Create(from, to, Solid);
        ray.HitBackFaces = false;   // a ray starting inside rock passes out through its underside instead of stopping there
        ray.HitFromInside = false;
        if (exclude.IsValid) ray.Exclude = new Godot.Collections.Array<Rid> { exclude };

        var hit = space.IntersectRay(ray);
        if (hit.Count == 0) return null;

        Vector3 normal = (Vector3)hit["normal"];
        if (normal.Y < MinFloorNormal) return null;

        // Lift the capsule so its rounded bottom clears the slope it's standing on, not just the contact point
        float slopeLift = radius * (1f / normal.Y - 1f);
        Vector3 centre = (Vector3)hit["position"] + Vector3.Up * (height / 2f + slopeLift + 0.05f);
        return Fits(space, centre, radius, height, exclude) ? centre : null;
    }

    private static bool Fits(PhysicsDirectSpaceState3D space, Vector3 centre, float radius, float height, Rid exclude)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(height, radius * 2f) },
            Transform = new Transform3D(Basis.Identity, centre),
            CollisionMask = Solid,
        };
        if (exclude.IsValid) query.Exclude = new Godot.Collections.Array<Rid> { exclude };
        return space.IntersectShape(query, 1).Count == 0;
    }
}
