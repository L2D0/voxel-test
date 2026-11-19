using Godot;

// The body every enemy shares. Its definition says what kind of enemy it is.
// The host runs it (movement, physics, damage, death); every other machine sees its
// position and facing through the MultiplayerSynchronizer, like a remote player.
public partial class Enemy : CharacterBody3D, IImpulseReceiver
{
    public EnemyDefinition Def { get; private set; }

    private Health _health;
    private StatusEffects _statuses;
    private Label3D _label;
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;

    private float _launchTimer;
    private float _corpseTimer = -1f;

    // Part 1 stand-in for real behaviour: wander in a random direction now and then,
    // so there are moving targets to test against. Part 2 replaces it with awareness states.
    private Vector3 _wanderDir;
    private float _wanderTimer;
    private readonly RandomNumberGenerator _rng = new();

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

        bool grounded = IsOnFloor() && _launchTimer <= 0f;
        Vector3 vel = Velocity;

        if (grounded)
        {
            Wander(dt);
            Vector3 horiz = new(vel.X, 0f, vel.Z);
            Vector3 wish = _wanderDir * Def.MoveSpeed;
            horiz = horiz.MoveToward(wish, Def.Acceleration * dt);
            vel.X = horiz.X;
            vel.Z = horiz.Z;
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

    private void Wander(float dt)
    {
        _wanderTimer -= dt;
        if (_wanderTimer > 0f) return;

        _wanderTimer = _rng.RandfRange(2f, 5f);
        bool stand = _rng.Randf() < 0.4f;   // sometimes just stand still
        float angle = _rng.Randf() * Mathf.Tau;
        _wanderDir = stand ? Vector3.Zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private void Face(float dt)
    {
        Vector3 horiz = new(Velocity.X, 0f, Velocity.Z);
        if (horiz.LengthSquared() < 0.25f) return;

        float target = Mathf.Atan2(-horiz.X, -horiz.Z);
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
        CreateTween().TweenProperty(_material, "albedo_color", Def?.Tint ?? Colors.Gray, 0.2);
    }

    private void OnDied(Node source)
    {
        Rotation = new Vector3(Mathf.Pi / 2f, Rotation.Y, 0f);   // placeholder: tip over
        _material.AlbedoColor = (Def?.Tint ?? Colors.Gray).Darkened(0.5f);
        _corpseTimer = Def?.CorpseTime ?? 4f;
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
        string text = $"{Def?.DisplayName ?? "Enemy"}\n{_health.Current:0} / {_health.Max:0}";
        if (_statuses != null)
            foreach (StatusEffects.Instance s in _statuses.Active)
                text += $"\n{s.Def.DisplayName} x{s.Stacks}";
        _label.Text = text;
    }
}
