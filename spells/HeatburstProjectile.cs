using Godot;
using System.Collections.Generic;

public partial class HeatburstProjectile : Node3D
{
    private const uint DamageableMask = (1u << 1) | (1u << 2);   // Characters + Structures

    public SpellContext Ctx;
    public Vector3 Direction;

    private PlayerController Caster => Ctx.Caster;

    // Stats are locked in at cast time
    private float _speed, _lifetime, _radius, _damage, _knockback;
    private float _selfKnockback, _selfUpwardBias, _selfDamageMult, _minFalloff;

    private float _age;
    private bool _exploded;

    public override void _Ready()
    {
        _speed = Ctx.Stat(nameof(HeatburstSpell.ProjectileSpeed));
        _lifetime = Ctx.Stat(nameof(HeatburstSpell.Lifetime));
        _radius = Ctx.Stat(nameof(HeatburstSpell.Radius));
        _damage = Ctx.Stat(nameof(HeatburstSpell.Damage));
        _knockback = Ctx.Stat(nameof(HeatburstSpell.Knockback));
        _selfKnockback = Ctx.Stat(nameof(HeatburstSpell.SelfKnockback));
        _selfUpwardBias = Ctx.Stat(nameof(HeatburstSpell.SelfUpwardBias));
        _selfDamageMult = Ctx.Stat(nameof(HeatburstSpell.SelfDamageMultiplier));
        _minFalloff = Ctx.Stat(nameof(HeatburstSpell.MinFalloff));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_exploded) return;
        float dt = (float)delta;

        // Raycast along this tick's movement so fast projectiles can't tunnel through thin surfaces
        Vector3 from = GlobalPosition;
        Vector3 to = from + Direction * _speed * dt;

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
        if (_age >= _lifetime) Explode(GlobalPosition);
    }

    private void Explode(Vector3 center)
    {
        _exploded = true;

        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = _radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = DamageableMask,
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

            if (!Ctx.Authoritative) continue;   // only the host decides hits on others
            Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(Ctx, _damage * falloff)); Ctx.ApplyStatuses(body);
            if (body is IImpulseReceiver receiver)
                receiver.AddImpulse(pushDir * _knockback * falloff);
        }

        ApplySelfHit(center);
        if (Ctx.Spell is HeatburstSpell spell && spell.LeavesEffect != null)
            GroundEffect.Spawn(spell.LeavesEffect, Caster, center);
        SpawnFlash(center);
        QueueFree();
    }

    private void ApplySelfHit(Vector3 center)
    {
        var capsule = Caster.GetNode<CollisionShape3D>("CollisionShape3D").Shape as CapsuleShape3D;
        float radius = capsule?.Radius ?? 0.5f;
        float halfSegment = (capsule?.Height ?? 2f) / 2f - radius;

        Vector3 p = Caster.GlobalPosition;
        Vector3 closest = Geometry3D.GetClosestPointToSegment(center, p + Vector3.Down * halfSegment, p + Vector3.Up * halfSegment);

        float distance = Mathf.Max(center.DistanceTo(closest) - radius, 0f);
        if (distance > _radius) return;

        float falloff = Falloff(distance);
        Vector3 offset = closest - center;
        Vector3 pushDir = offset.LengthSquared() > 0.0001f ? offset.Normalized() : Vector3.Up;
        pushDir = (pushDir + Vector3.Up * _selfUpwardBias).Normalized();

        // The host applies the damage; the caster's own machine applies the launch, so jumps have no lag
        if (Ctx.Authoritative) Health.Of(Caster)?.TakeDamage(DamageInfo.FromSpell(Ctx, _damage * falloff * _selfDamageMult));
        if (Caster.IsMultiplayerAuthority()) Caster.AddImpulse(pushDir * _selfKnockback * falloff);
    }

    private float Falloff(float distance) =>
        Mathf.Lerp(1f, _minFalloff, Mathf.Clamp(distance / _radius, 0f, 1f));

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
        tween.TweenProperty(flash, "scale", Vector3.One * _radius, 0.18);
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.25);
        tween.Chain().TweenCallback(Callable.From(flash.QueueFree));
    }
}