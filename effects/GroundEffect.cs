using Godot;
using System.Collections.Generic;

// A lingering area left by a spell. Every machine runs a copy, so stat modifiers apply
// locally; only the host deals damage and applies statuses.
public partial class GroundEffect : Area3D
{
    private const uint WorldLayer = 1u;
    private const uint CharacterLayer = 1u << 1;
    private const float FadeTime = 0.5f;

    public static readonly List<GroundEffect> Active = new();

    public GroundEffectDefinition Def;
    public Node3D Caster;
    public Vector3 Drift;   // metres per second, for zones that move

    private bool _casterIsPlayer;
    private float _radius, _age, _tickTimer;
    private CollisionShape3D _shape;
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private readonly HashSet<Node3D> _inside = new();

    // ---------- Spawning ----------

    public static GroundEffect Spawn(GroundEffectDefinition def, Node3D caster, Vector3 position, Vector3 drift = default)
    {
        if (def == null || caster == null) return null;

        if (def.Shape == EffectShape.Pool)
        {
            // Pools sit on the ground: drop onto whatever's below, or don't spawn at all
            var query = PhysicsRayQueryParameters3D.Create(
                position + Vector3.Up * 0.5f, position + Vector3.Down * def.MaxDropDistance, WorldLayer);
            var hit = caster.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0) return null;
            position = (Vector3)hit["position"];
        }

        var effect = new GroundEffect { Def = def, Caster = caster, Drift = drift };
        caster.GetTree().CurrentScene.AddChild(effect);
        effect.GlobalPosition = position;
        return effect;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = CharacterLayer;
        Monitorable = false;

        _casterIsPlayer = Caster.IsInGroup("players");
        _radius = Def.Radius;
        _tickTimer = Def.TickInterval;

        bool pool = Def.Shape == EffectShape.Pool;
        _shape = new CollisionShape3D
        {
            Shape = pool ? new CylinderShape3D { Height = Def.Height } : new SphereShape3D(),
            Position = pool ? Vector3.Up * Def.Height / 2f : Vector3.Zero,
        };
        AddChild(_shape);

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = Def.Color,
        };
        _mesh = new MeshInstance3D
        {
            Mesh = pool ? new CylinderMesh { Height = 0.08f } : new SphereMesh(),
            Position = pool ? Vector3.Up * 0.05f : Vector3.Zero,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);

        ApplySize();
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        Active.Add(this);
    }

    public override void _ExitTree()
    {
        Active.Remove(this);
        foreach (Node3D body in _inside) RemoveModifiers(body);
        _inside.Clear();
    }

    // ---------- Who's inside ----------

    private bool IsAlly(Node3D body) =>
        body == Caster || body.IsInGroup("players") == _casterIsPlayer;

    private bool IsHarmed(Node3D body) =>
        body == Caster ? Def.HarmsCaster : !IsAlly(body);

    private void OnBodyEntered(Node3D body)
    {
        if (!_inside.Add(body)) return;
        if (!IsAlly(body) || body is not PlayerController player) return;

        var mods = player.GetNodeOrNull<ModifierStack>("Modifiers");
        if (mods == null) return;
        foreach (StatModifier mod in Def.AllyModifiers)
            if (mod != null) mods.Add(mod, this);
    }

    private void OnBodyExited(Node3D body)
    {
        if (_inside.Remove(body)) RemoveModifiers(body);
    }

    private void RemoveModifiers(Node3D body)
    {
        if (Def.AllyModifiers.Count == 0 || !IsInstanceValid(body)) return;
        body.GetNodeOrNull<ModifierStack>("Modifiers")?.RemoveSource(this);
    }

    // Is this body standing in any effect with this tag? ("toxic" for Virality, and so on)
    public static bool IsInsideTagged(Node3D body, string tag)
    {
        foreach (GroundEffect effect in Active)
            if (effect._inside.Contains(body) && effect.Def.Tags.Contains(tag)) return true;
        return false;
    }

    // ---------- Lifetime ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_age >= Def.Duration)
        {
            QueueFree();
            return;
        }

        if (Drift != Vector3.Zero) GlobalPosition += Drift * dt;

        if (Def.GrowthPerSecond != 0f)
        {
            _radius += Def.GrowthPerSecond * dt;
            if (Def.MaxRadius > 0f) _radius = Mathf.Min(_radius, Def.MaxRadius);
            ApplySize();
        }

        float fade = Mathf.Clamp((Def.Duration - _age) / FadeTime, 0f, 1f);
        _material.AlbedoColor = new Color(Def.Color, Def.Color.A * fade);

        if (!Multiplayer.IsServer()) return;   // only the host hurts and applies statuses

        _tickTimer -= dt;
        if (_tickTimer > 0f) return;
        _tickTimer += Def.TickInterval;
        Tick();
    }

    private void Tick()
    {
        Node source = IsInstanceValid(Caster) ? Caster : null;

        foreach (Node3D body in new List<Node3D>(_inside))
        {
            if (!IsInstanceValid(body))
            {
                _inside.Remove(body);
                continue;
            }

            if (IsHarmed(body))
            {
                if (Def.Damage > 0f)
                    Health.Of(body)?.TakeDamage(new DamageInfo
                    {
                        Amount = Def.Damage,
                        Type = Def.DamageType,
                        Source = source,
                        CanKill = Def.CanKill,
                    });
                ApplyStatuses(body, Def.EnemyStatuses, source);
            }

            if (IsAlly(body)) ApplyStatuses(body, Def.AllyStatuses, source);
        }
    }

    private static void ApplyStatuses(Node3D body, Godot.Collections.Array<StatusDefinition> statuses, Node source)
    {
        if (statuses.Count == 0) return;
        var effects = StatusEffects.Of(body);
        if (effects == null) return;
        foreach (StatusDefinition status in statuses)
            if (status != null) effects.Apply(status, source);
    }

    private void ApplySize()
    {
        if (_shape.Shape is CylinderShape3D cylinder) cylinder.Radius = _radius;
        if (_shape.Shape is SphereShape3D sphere) sphere.Radius = _radius;

        if (_mesh.Mesh is CylinderMesh disc)
        {
            disc.TopRadius = _radius;
            disc.BottomRadius = _radius;
        }
        if (_mesh.Mesh is SphereMesh ball)
        {
            ball.Radius = _radius;
            ball.Height = _radius * 2f;
        }
    }
}