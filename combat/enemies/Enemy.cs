using Godot;

// The body every enemy shares. Its definition says what kind of enemy it is.
// The host runs it (movement, physics, damage, death, the brain in Enemy.Awareness.cs,
// abilities in Enemy.Abilities.cs);
// every other machine sees its position and facing through the MultiplayerSynchronizer.
public partial class Enemy : CharacterBody3D, IImpulseReceiver
{
    public EnemyDefinition Def { get; private set; }
    public Vector3 Home { get; private set; }   // where it spawned; wandering and the leash centre on it

    private Health _health;
    private StatusEffects _statuses;
    private Label3D _label;
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;

    private float _launchTimer;
    private float _corpseTimer = -1f;

    // What the brain asks the body to do each frame
    private Vector3? _moveTo;
    private float _arriveDistance = 0.5f;
    private float _speedScale = 1f;
    private Vector3? _lookAt;

    // Stamina: anything faster than walking drains it. Run dry and it's tired until it's full again.
    private float _stamina;
    private bool _exhausted;

    // ---------- Setup ----------

    // Called on every machine by the spawner, before the enemy enters the tree
    public void Setup(EnemyDefinition def)
    {
        Def = def;

        var health = GetNode<Health>("Health");
        health.MaxHealth = def.MaxHealth;
        health.Resistances = def.Resistances;

        GetNode<EntityInfo>("EntityInfo").Weight = def.Weight;
    }

    public override void _Ready()
    {
        AddToGroup("enemies");
        Home = GlobalPosition;

        _health = Health.Of(this);
        _statuses = StatusEffects.Of(this);
        _label = GetNode<Label3D>("HealthLabel");
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");

        _material = new StandardMaterial3D { AlbedoColor = Def?.Tint ?? Colors.Gray };
        _mesh.MaterialOverride = _material;

        _health.Damaged += OnDamaged;
        _health.Changed += (current, max) => UpdateLabel();
        _health.Died += OnDied;
        if (_statuses != null) _statuses.Changed += UpdateLabel;

        _rng.Randomize();
        _senseTimer = _rng.Randf() * SenseInterval;   // spread the brains out so they don't all think on one frame
        _stamina = Def?.MaxStamina ?? 0f;
        UpdateLabel();

        // Only the host moves enemies; everyone else is told where they are
        SetPhysicsProcess(Multiplayer.IsServer());
    }

    // ---------- Movement (host) ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _launchTimer -= dt;

        if (_health.IsDead)
        {
            TickCorpse(dt);
            ApplyGravityOnly(dt);
            return;
        }

        Think(dt);

        bool grounded = IsOnFloor() && _launchTimer <= 0f;
        Vector3 vel = Velocity;

        if (grounded)
        {
            Vector3 direction = WishDirection();
            Vector3 wish = direction * Def.MoveSpeed * SpeedScale(dt, direction != Vector3.Zero);
            Vector3 horiz = new Vector3(vel.X, 0f, vel.Z).MoveToward(wish, Def.Acceleration * dt);
            vel.X = horiz.X;
            vel.Z = horiz.Z;

            // Walked into a step or a wall: hop. Real pathfinding comes later.
            if (wish != Vector3.Zero && IsOnWall()) vel.Y = Def.JumpVelocity;
        }
        else
        {
            vel += GetGravity() * dt;   // no air control: knockback carries them
        }

        bool wasAirborne = !IsOnFloor();
        float fallSpeed = Mathf.Max(-vel.Y, 0f);

        Velocity = vel;
        MoveAndSlide();

        if (wasAirborne && IsOnFloor()) Land(fallSpeed);
        Face(dt);
    }

    // The brain's handles on the body
    private void MoveTo(Vector3 point, float speedScale = 1f, float arrive = 0.5f)
    {
        _moveTo = point;
        _speedScale = speedScale;
        _arriveDistance = arrive;
    }

    private void Stop() => _moveTo = null;
    private void LookToward(Vector3 point) => _lookAt = point;
    private void StopLooking() => _lookAt = null;

    private bool Arrived(Vector3 point, float distance = 0.75f) => Flat(point - GlobalPosition).Length() <= distance;

    private Vector3 WishDirection()
    {
        if (_moveTo is not Vector3 target) return Vector3.Zero;
        Vector3 to = Flat(target - GlobalPosition);
        return to.Length() <= _arriveDistance ? Vector3.Zero : to.Normalized();
    }

    // The speed the brain asked for, limited by stamina
    private float SpeedScale(float dt, bool moving)
    {
        if (Def.MaxStamina <= 0f) return _speedScale;   // 0 = never tires

        float scale = _exhausted ? Mathf.Min(_speedScale, Def.TiredSpeedScale) : _speedScale;
        if (moving && scale > 1f)
        {
            _stamina -= dt;
            if (_stamina <= 0f)
            {
                _stamina = 0f;
                _exhausted = true;
            }
        }
        else
        {
            _stamina = Mathf.Min(_stamina + Def.StaminaRegen * dt, Def.MaxStamina);
            if (_stamina >= Def.MaxStamina) _exhausted = false;
        }
        return scale;
    }

    // Faces what it's looking at if anything, otherwise where it's going
    private void Face(float dt)
    {
        Vector3 dir = _lookAt is Vector3 look ? Flat(look - GlobalPosition) : Flat(Velocity);
        if (dir.LengthSquared() < 0.25f) return;

        float target = Mathf.Atan2(-dir.X, -dir.Z);
        Vector3 rot = Rotation;
        rot.Y = Mathf.LerpAngle(rot.Y, target, Def.TurnSpeed * dt);
        Rotation = rot;
    }

    private void ApplyGravityOnly(float dt)
    {
        Vector3 vel = Velocity;
        vel += GetGravity() * dt;
        if (IsOnFloor()) vel = new Vector3(vel.X * 0.8f, vel.Y, vel.Z * 0.8f);   // bodies slide to a stop
        Velocity = vel;
        MoveAndSlide();
    }

    // Same rule as players: impact speed, not height. A lethal-speed landing takes all its max health.
    private void Land(float speed)
    {
        if (speed <= Def.SafeFallSpeed) return;

        float t = Mathf.Clamp((speed - Def.SafeFallSpeed) / (Def.LethalFallSpeed - Def.SafeFallSpeed), 0f, 1f);
        _health.TakeDamage(new DamageInfo { Amount = _health.Max * Mathf.Pow(t, 1.5f), Type = DamageType.Physical });
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    // ---------- Knockback ----------

    // Spell code that pushes enemies only runs on the host, so the host applies it straight away
    public void AddImpulse(Vector3 impulse)
    {
        if (!Multiplayer.IsServer()) return;

        Velocity += impulse * EntityInfo.KnockbackScale(this);
        _launchTimer = 0.15f;   // ignore the ground briefly so friction can't eat the launch
    }

    // ---------- Damage and death ----------

    private void OnDamaged(float amount, Node source)
    {
        _material.AlbedoColor = Colors.White;
        CreateTween().TweenProperty(_material, "albedo_color", BodyColor(), 0.2);

        if (Multiplayer.IsServer()) ReactToDamage(amount, source as Node3D);   // the source is only known on the host
    }

    private void OnDied(Node source)
    {
        Rotation = new Vector3(Mathf.Pi / 2f, Rotation.Y, 0f);   // placeholder: tip over
        _telegraphing = false;
        _windingUp = -1;
        _material.AlbedoColor = BodyColor();
        _corpseTimer = Def?.CorpseTime ?? 4f;
        _threat.Clear();
        UpdateLabel();
        // Loot drops go here once items exist (pipeline 6.9)
    }

    // Host only. Freeing a node the spawner created removes it on every machine.
    private void TickCorpse(float dt)
    {
        _corpseTimer -= dt;
        if (_corpseTimer <= 0f) QueueFree();
    }

    private void UpdateLabel()
    {
        string text = $"{Def?.DisplayName ?? "Enemy"}";
        if (!_health.IsDead && _shownState != AwareState.Idle) text += $"  [{_shownState}]";
        text += $"\n{_health.Current:0} / {_health.Max:0}";
        if (_statuses != null)
            foreach (StatusEffects.Instance s in _statuses.Active)
                text += $"\n{s.Def.DisplayName} x{s.Stacks}";
        _label.Text = text;
    }
}
