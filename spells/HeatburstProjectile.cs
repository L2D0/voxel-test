using Godot;
using System.Collections.Generic;

public partial class HeatburstProjectile : Node3D
{
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

        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = Spell.Radius },
            Transform = new Transform3D(Basis.Identity, center),
        };
        var results = GetWorld3D().DirectSpaceState.IntersectShape(shapeQuery, 32);

        var alreadyHit = new HashSet<Node>();
        foreach (var result in results)
        {
            var body = result["collider"].As<Node3D>();
            if (body == null || !alreadyHit.Add(body)) continue;

            Vector3 offset = body.GlobalPosition - center;
            float distance = offset.Length();
            float falloff = Mathf.Lerp(1f, Spell.MinFalloff, Mathf.Clamp(distance / Spell.Radius, 0f, 1f));
            Vector3 pushDir = distance > 0.01f ? offset / distance : Vector3.Up;

            bool isCaster = body == Caster;
            float damage = Spell.Damage * falloff * (isCaster ? Spell.SelfDamageMultiplier : 1f);
            float knockback = (isCaster ? Spell.SelfKnockback : Spell.Knockback) * falloff;

            Health.Of(body)?.TakeDamage(damage, Caster);
            if (body is IImpulseReceiver receiver)
                receiver.AddImpulse(pushDir * knockback);
        }

        SpawnFlash(center);
        QueueFree();
    }

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