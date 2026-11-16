using Godot;
using System.Collections.Generic;

public partial class StonePillar : AnimatableBody3D
{
    private const uint WorldLayer = 1u << 0;
    private const uint StructureLayer = 1u << 2;

    public SpellContext Ctx;
    public Vector3 Axis = Vector3.Up;

    private enum State { Windup, Rising, Standing, Crumbling }

    // Stats are locked in at cast time
    private float _maxHeight, _minHeight, _riseSpeed, _windup, _lifetime, _warningTime, _radius, _ringHeight;

    private const uint CharacterLayer = 1u << 1;

    private State _state = State.Windup;
    private float _timer;
    private float _risen;
    private bool _released;
    private readonly List<MeshInstance3D> _rings = new();
    private readonly List<CollisionShape3D> _shapes = new();
    private readonly RandomNumberGenerator _rng = new();

    private void SeismicBurst()
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 4f },
            Transform = new Transform3D(Basis.Identity, GlobalPosition - Axis * (_risen / 2f)),
            CollisionMask = CharacterLayer,
        };

        var hits = new HashSet<Node>();
        foreach (var result in GetWorld3D().DirectSpaceState.IntersectShape(query, 32))
        {
            var body = result["collider"].As<Node>();
            if (body == null || body == Ctx.Caster || !hits.Add(body)) continue;
            Health.Of(body)?.TakeDamage(25f, Ctx.Caster);
        }
    }

    public override void _Ready()
    {
        _maxHeight = Ctx.Stat(nameof(StonePillarSpell.MaxHeight));
        _minHeight = Ctx.Stat(nameof(StonePillarSpell.MinHeight));
        _riseSpeed = Ctx.Stat(nameof(StonePillarSpell.RiseSpeed));
        _windup = Ctx.Stat(nameof(StonePillarSpell.Windup));
        _lifetime = Ctx.Stat(nameof(StonePillarSpell.Lifetime));
        _warningTime = Ctx.Stat(nameof(StonePillarSpell.WarningTime));
        _radius = Ctx.Stat(nameof(StonePillarSpell.PillarRadius));
        _ringHeight = Ctx.Stat(nameof(StonePillarSpell.RingHeight));

        CollisionLayer = WorldLayer | StructureLayer;
        CollisionMask = 0;
        _rng.Randomize();

        var health = new Health { Name = "Health", MaxHealth = Ctx.Stat(nameof(StonePillarSpell.MaxHealth)) };
        AddChild(health);
        health.Died += source => Crumble();

        AddRing();
        AddRing();
    }

    public void StopRising() => _released = true;

    // Ring 0 is the top; each new ring is stacked underneath
    private Vector3 RingBasePosition(int i) => new Vector3(0, -(i + 0.5f) * _ringHeight, 0);

    private void AddRing()
    {
        int i = _rings.Count;
        float r = _radius * _rng.RandfRange(0.85f, 1.1f);

        var ring = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = r * _rng.RandfRange(0.85f, 1f),
                BottomRadius = r,
                Height = _ringHeight,
                RadialSegments = 7,
                Rings = 1,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.47f, 0.43f, 0.38f).Darkened(_rng.RandfRange(0f, 0.25f)),
            },
            Position = RingBasePosition(i),
            Rotation = new Vector3(0, _rng.Randf() * Mathf.Tau, 0),
        };
        AddChild(ring);
        _rings.Add(ring);

        var shape = new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = r, Height = _ringHeight },
            Position = RingBasePosition(i),
        };
        AddChild(shape);
        _shapes.Add(shape);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _timer += dt;

        switch (_state)
        {
            case State.Windup:
                if (_timer >= _windup)
                {
                    _state = State.Rising;
                    _timer = 0f;
                }
                break;

            case State.Rising:
                bool done = (_released && _risen >= _minHeight) || _risen >= _maxHeight;
                if (done)
                {
                    _state = State.Standing;
                    _timer = 0f;
                    Shake(0f);
                    break;
                }

                float step = Mathf.Min(_riseSpeed * dt, _maxHeight - _risen);
                _risen += step;
                GlobalPosition += Axis * step;
                Shake(0.15f);

                while (_rings.Count * _ringHeight < _risen + _ringHeight * 1.5f)
                    AddRing();
                break;

            case State.Standing:
                float remaining = _lifetime - _timer;
                Shake(remaining < _warningTime ? 1f - remaining / _warningTime : 0f);
                if (remaining <= 0f) Crumble();
                break;
        }
    }

    private void Shake(float intensity)
    {
        float amount = 0.06f * intensity;
        for (int i = 0; i < _rings.Count; i++)
            _rings[i].Position = RingBasePosition(i) + new Vector3(
                _rng.RandfRange(-amount, amount), 0f, _rng.RandfRange(-amount, amount));
    }

    public void Crumble()
    {
        if (_state == State.Crumbling) return;
        _state = State.Crumbling;

        if (Ctx.HasFlag("SeismicCrumble")) SeismicBurst();

        foreach (CollisionShape3D shape in _shapes)
            shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);

        Tween tween = CreateTween().SetParallel();
        for (int i = 0; i < _rings.Count; i++)
        {
            MeshInstance3D ring = _rings[i];
            float delay = i * 0.04f;
            Vector3 drop = ring.Position + new Vector3(_rng.RandfRange(-0.4f, 0.4f), -1.5f, _rng.RandfRange(-0.4f, 0.4f));

            tween.TweenProperty(ring, "position", drop, 0.5).SetDelay(delay)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            tween.TweenProperty(ring, "scale", Vector3.One * 0.1f, 0.5).SetDelay(delay);
        }
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}