using Godot;
using System.Collections.Generic;

// Earth pushed out of a surface: pillars now, barriers and spikes later. It rises segment by segment
// from just under the surface (so it never pokes into a cave below), stands for a while, then crumbles.
// Every machine runs its own copy. Anything that affects collision is seeded from the node's name,
// so every copy has exactly the same shape.
public partial class Extrusion : AnimatableBody3D
{
    public SpellContext Ctx;
    public Vector3 Axis = Vector3.Up;
    public Extrusion Foundation;   // the extrusion it was raised from, if any

    // Filled in by the spell from its stats, before it's added to the tree
    public float MaxHeight = 6f;
    public float MinHeight = 1f;
    public float RiseSpeed = 4f;
    public float Windup = 0.25f;
    public float Lifetime = 5f;
    public float WarningTime = 1.5f;
    public float SegmentHeight = 0.75f;
    public float MaxHealth = 100f;

    public event System.Action Crumbled;

    protected enum State { Windup, Rising, Standing, Crumbling }
    protected State Phase { get; private set; } = State.Windup;
    protected float Risen { get; private set; }

    protected readonly RandomNumberGenerator ShapeRng = new();   // seeded: the same on every machine
    protected readonly RandomNumberGenerator FxRng = new();      // cosmetic only: shaking, crumbling

    private float _timer;
    private float _targetHeight = -1f;   // unknown until released; until then it rises toward MaxHeight
    private readonly List<MeshInstance3D> _segments = new();
    private readonly List<CollisionShape3D> _shapes = new();

    // Seconds left standing. Before it's finished rising, its full lifetime.
    public float Remaining => Phase switch
    {
        State.Standing => Mathf.Max(Lifetime - _timer, 0f),
        State.Crumbling => 0f,
        _ => Lifetime,
    };

    // ---------- Setup ----------

    public override void _Ready()
    {
        CollisionLayer = Solid ? SpellFx.WorldLayer | SpellFx.StructureLayer : 0;
        CollisionMask = 0;
        ShapeRng.Seed = (ulong)(uint)GD.Hash(Name.ToString());
        FxRng.Randomize();

        var health = new Health { Name = "Health", MaxHealth = MaxHealth };
        health.Immunities.Add(DamageType.Shock);   // stone
        AddChild(health);
        health.Died += source => Crumble();

        // Raised from another extrusion: it can't outlast it, and goes down with it
        if (Foundation != null)
        {
            Lifetime = Mathf.Min(Lifetime, Foundation.Remaining);
            Foundation.Crumbled += Crumble;
        }

        AddSegment();
        AddSegment();
    }

    public override void _ExitTree()
    {
        if (Foundation != null && IsInstanceValid(Foundation)) Foundation.Crumbled -= Crumble;
    }

    // The extrusion directly under a cast point, if the cast was aimed at one.
    // Every machine runs this itself, since the hit collider isn't sent over the network.
    public static Extrusion FoundationAt(Node3D from, Vector3 point, Vector3 normal)
    {
        var query = PhysicsRayQueryParameters3D.Create(point + normal * 0.2f, point - normal * 0.3f, SpellFx.StructureLayer);
        var hit = from.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["collider"].AsGodotObject() as Extrusion : null;
    }

    // ---------- Segments ----------

    // What one segment looks like and collides as. Subclasses decide the shape; use ShapeRng for any variation.
    protected virtual (Mesh mesh, Shape3D shape, Material material) MakeSegment(int index)
    {
        var size = new Vector3(1f, SegmentHeight, 1f);
        return (new BoxMesh { Size = size }, new BoxShape3D { Size = size }, null);
    }

    // Round shapes look better twisted at random; flat ones (walls) shouldn't be
    protected virtual bool RandomYaw => false;

    // Visual-only extrusions (Expulsion's spike) get no collision at all
    protected virtual bool Solid => true;

    // Segment 0 is the top; each new one is stacked underneath as it rises
    private Vector3 SegmentPosition(int i) => new(0f, -(i + 0.5f) * SegmentHeight, 0f);

    private void AddSegment()
    {
        int i = _segments.Count;
        var (mesh, shape, material) = MakeSegment(i);
        float yaw = RandomYaw ? ShapeRng.Randf() * Mathf.Tau : 0f;

        var visual = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = SegmentPosition(i),
            Rotation = new Vector3(0f, yaw, 0f),
        };
        AddChild(visual);
        _segments.Add(visual);
        if (!Solid) return;   // visual only: no collision

        var collider = new CollisionShape3D { Shape = shape, Position = SegmentPosition(i) };
        AddChild(collider);
        _shapes.Add(collider);
    }

    // ---------- Rising ----------

    // Hold casts: runs on every machine with the owner's held time, so every copy stops at the same height
    public void Release(float heldTime)
    {
        float rising = Mathf.Max(0f, heldTime - Windup);
        _targetHeight = Mathf.Clamp(rising * RiseSpeed, MinHeight, MaxHeight);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _timer += dt;

        switch (Phase)
        {
            case State.Windup:
                if (_timer >= Windup)
                {
                    Phase = State.Rising;
                    _timer = 0f;
                }
                break;

            case State.Rising:
                // Moves toward the goal from either side: a copy that rose a little too far
                // before the release arrived sinks back to match the owner's
                float goal = _targetHeight >= 0f ? _targetHeight : MaxHeight;
                float next = Mathf.MoveToward(Risen, goal, RiseSpeed * dt);
                GlobalPosition += Axis * (next - Risen);
                Risen = next;

                while (_segments.Count * SegmentHeight < Risen + SegmentHeight * 1.5f)
                    AddSegment();

                if (Mathf.Abs(goal - Risen) < 0.001f)
                {
                    Phase = State.Standing;
                    _timer = 0f;
                    Shake(0f);
                }
                else Shake(0.15f);
                break;

            case State.Standing:
                float remaining = Lifetime - _timer;
                Shake(remaining < WarningTime ? 1f - remaining / WarningTime : 0f);
                if (remaining <= 0f) Crumble();
                break;
        }
    }

    private void Shake(float intensity)
    {
        float amount = 0.06f * intensity;
        for (int i = 0; i < _segments.Count; i++)
            _segments[i].Position = SegmentPosition(i) + new Vector3(
                FxRng.RandfRange(-amount, amount), 0f, FxRng.RandfRange(-amount, amount));
    }

    // ---------- Crumbling ----------

    // Hook for whatever happens as it breaks (Seismic crumble's burst)
    protected virtual void OnCrumble() { }

    public void Crumble()
    {
        if (Phase == State.Crumbling) return;
        Phase = State.Crumbling;

        OnCrumble();
        Crumbled?.Invoke();   // anything built on top of it comes down too

        foreach (CollisionShape3D shape in _shapes)
            shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);

        Tween tween = CreateTween().SetParallel();
        for (int i = 0; i < _segments.Count; i++)
        {
            MeshInstance3D segment = _segments[i];
            float delay = i * 0.04f;
            Vector3 drop = segment.Position + new Vector3(FxRng.RandfRange(-0.4f, 0.4f), -1.5f, FxRng.RandfRange(-0.4f, 0.4f));

            tween.TweenProperty(segment, "position", drop, 0.5).SetDelay(delay)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            tween.TweenProperty(segment, "scale", Vector3.One * 0.1f, 0.5).SetDelay(delay);
        }
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
