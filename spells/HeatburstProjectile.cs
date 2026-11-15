using Godot;
using System.Collections.Generic;

public partial class HeatburstProjectile : Node3D
{
    private const uint CharacterLayer = 1u << 1;   // physics layer 2

    public HeatburstSpell Spell;
    public PlayerController Caster;
    public Vector3 Direction;

    private float _age;
    private bool _exploded;

    public override void _PhysicsProcess(double delta)
    {
        if (_exploded) return;
        float dt = (float)delta;

        // Raycast along this tick's movement so fast projectiles can't tunnel through thin surfaces
        Vector3 from = GlobalPosition;
        Vector3 to = from + Direction * Spell.ProjectileSpeed * dt;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { Caster.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (hit.Count > 0)
        {
            Explode((Vector3)hit["position"]);
            return;
        }

        GlobalPosition = to;
        _age += dt;
        if (_age >= Spell.Lifetime) Explode(GlobalPosition);
    }

    private void Explode(Vector3 center)
    {
        _exploded = true;

        // Only look for characters, never terrain
        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = Spell.Radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = CharacterLayer,
        };
        var results = GetWorld3D().DirectSpaceState.IntersectShape(shapeQuery, 32);

        var alreadyHit = new HashSet<Node>();
        foreach (var result in results)
        {
            var body = result["collider"].As<Node3D>();
            if (body == null || body == Caster || !alreadyHit.Add(body)) continue;

            Vector3 offset = body.GlobalPosition - center;
            float distance = offset.Length();
            float falloff = Falloff(distance);
            Vector3 pushDir = distance > 0.01f ? offset / distance : Vector3.Up;

            Health.Of(body)?.TakeDamage(Spell.Damage * falloff, Caster);
            if (body is IImpulseReceiver receiver)
                receiver.AddImpulse(pushDir * Spell.Knockback * falloff);
        }

        ApplySelfHit(center);
        SpawnFlash(center);
        QueueFree();
    }

    private void ApplySelfHit(Vector3 center)
    {
        // Measure to the closest point on the caster's capsule, not its centre
        var capsule = Caster.GetNode<CollisionShape3D>("CollisionShape3D").Shape as CapsuleShape3D;
        float radius = capsule?.Radius ?? 0.5f;
        float halfSegment = (capsule?.Height ?? 2f) / 2f - radius;

        Vector3 p = Caster.GlobalPosition;
        Vector3 closest = Geometry3D.GetClosestPointToSegment(center, p + Vector3.Down * halfSegment, p + Vector3.Up * halfSegment);

        float distance = Mathf.Max(center.DistanceTo(closest) - radius, 0f);
        if (distance > Spell.Radius) return;

        float falloff = Falloff(distance);
        Vector3 offset = closest - center;
        Vector3 pushDir = offset.LengthSquared() > 0.0001f ? offset.Normalized() : Vector3.Up;
        pushDir = (pushDir + Vector3.Up * Spell.SelfUpwardBias).Normalized();

        Health.Of(Caster)?.TakeDamage(Spell.Damage * falloff * Spell.SelfDamageMultiplier, Caster);
        Caster.AddImpulse(pushDir * Spell.SelfKnockback * falloff);
    }

    private float Falloff(float distance) =>
        Mathf.Lerp(1f, Spell.MinFalloff, Mathf.Clamp(distance / Spell.Radius, 0f, 1f));

    private void SpawnFlash(Vector3 center)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.6f),
        };
        var flash = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f },
            MaterialOverride = material,
        };
        GetTree().CurrentScene.AddChild(flash);
        flash.GlobalPosition = center;
        flash.Scale = Vector3.One * 0.2f;

        Tween tween = flash.CreateTween().SetParallel();
        tween.TweenProperty(flash, "scale", Vector3.One * Spell.Radius, 0.18);
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.25);
        tween.Chain().TweenCallback(Callable.From(flash.QueueFree));
    }
}