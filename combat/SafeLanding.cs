using Godot;

// Finds somewhere a body can stand near a point. Every teleport goes through this (Electromotive
// rush, Shadow step, Guiding light, waystones), so none of them can drop you inside terrain,
// inside a pillar, on a cliff face, or out over the void.
public static class SafeLanding
{
    private const uint Solid = SpellFx.WorldLayer | SpellFx.StructureLayer;   // terrain and extrusions
    private const float ProbeAbove = 4f;     // look for ground starting this far above the target...
    private const float ProbeBelow = 12f;    // ...down to this far below it
    private const float MinFloorNormal = 0.6f;   // steeper than about 53 degrees isn't standing ground
    private const int Rings = 4;             // if the exact spot fails, try rings around it
    private const float RingStep = 1f;       // metres between rings
    private const int PointsPerRing = 8;     // the first ring; each ring further out tries more

    // Where the centre of a capsule this size can go, as close to target as possible.
    // Null if nothing within a few metres works. exclude: the body being moved, so it doesn't block itself.
    public static Vector3? Find(Node3D context, Vector3 target, float radius, float height, Rid exclude = default)
    {
        PhysicsDirectSpaceState3D space = context.GetWorld3D().DirectSpaceState;

        if (TryColumn(space, target, radius, height, exclude) is Vector3 spot) return spot;

        for (int ring = 1; ring <= Rings; ring++)
        {
            int count = PointsPerRing * ring;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Tau * i / count;
                Vector3 offset = new(Mathf.Cos(angle) * ring * RingStep, 0f, Mathf.Sin(angle) * ring * RingStep);
                if (TryColumn(space, target + offset, radius, height, exclude) is Vector3 found) return found;
            }
        }
        return null;
    }

    // Drops a ray down one column. If it lands on standing ground and the capsule fits there, that's the spot.
    private static Vector3? TryColumn(PhysicsDirectSpaceState3D space, Vector3 point, float radius, float height, Rid exclude)
    {
        var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * ProbeAbove, point + Vector3.Down * ProbeBelow, Solid);
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
