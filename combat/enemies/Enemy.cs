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
    private MeshInstance3D _mesh;            // the placeholder capsule
    private StandardMaterial3D _material;    // its colour
    private Node3D _model;                   // the definition's model, if it has one
    private AnimationPlayer _anim;
    private StandardMaterial3D _overlay;     // tints a model without touching its textures: flash, telegraph, corpse
    private float _hopTimer;

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

        // Body size. Shapes are shared between instances of the scene, so each enemy gets its own.
        float height = Mathf.Max(def.BodyHeight, def.BodyRadius * 2f);
        GetNode<CollisionShape3D>("CollisionShape3D").Shape = new CapsuleShape3D { Radius = def.BodyRadius, Height = height };
        var placeholder = GetNode<MeshInstance3D>("MeshInstance3D");
        placeholder.Mesh = new CapsuleMesh { Radius = def.BodyRadius, Height = height };
        GetNode<Node3D>("HealthLabel").Position = new Vector3(0f, height / 2f + 0.5f, 0f);

        if (def.Model == null) return;

        // The origin is the middle of the capsule; models are built standing on y = 0
        placeholder.Visible = false;
        _model = def.Model.Instantiate<Node3D>();
        _model.Name = "Model";
        _model.Position = new Vector3(0f, -height / 2f, 0f);
        _model.Scale = Vector3.One * def.ModelScale;
        AddChild(_model);
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
        _overlay = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Colors.Transparent,
        };
        if (_model != null) SetupModel();

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

        if (_statuses?.IsStunned ?? false)
        {
            CancelWindup();   // a stun interrupts an attack
            Stop();
            StopLooking();
        }
        else Think(dt);

        Vector3 pose = Rotation;
        pose.X = (_statuses?.IsKnockedDown ?? false) ? Mathf.Pi / 2f : 0f;   // placeholder: lying down
        Rotation = pose;
        bool grounded = IsOnFloor() && _launchTimer <= 0f;
        Vector3 vel = Velocity;

        if (grounded)
        {
            Vector3 direction = WishDirection();
            float scale = SpeedScale(dt, direction != Vector3.Zero) * (1f - (_statuses?.Slow ?? 0f));

            if (Def.Movement == MovementStyle.Hop)
            {
                vel = Hop(vel, direction, scale, dt);
            }
            else
            {
                Vector3 wish = direction * Def.MoveSpeed * scale;
                Vector3 horiz = new Vector3(vel.X, 0f, vel.Z).MoveToward(wish, Def.Acceleration * dt);
                vel.X = horiz.X;
                vel.Z = horiz.Z;

                // Walked into a step or a wall: hop. Real pathfinding comes later.
                if (wish != Vector3.Zero && IsOnWall()) vel.Y = Def.JumpVelocity;
            }
        }
        else
        {
            vel += GetGravity() * dt;   // no air control: knockback carries them
            vel.Y = Mathf.Max(vel.Y, -Def.TerminalVelocity);
        }

        bool wasAirborne = !IsOnFloor();
        float fallSpeed = Mathf.Max(-vel.Y, 0f);

        Velocity = vel;
        MoveAndSlide();

        if (wasAirborne && IsOnFloor()) Land(fallSpeed);
        Face(dt);
    }

    // Hop movement: sits still between hops, then leaps toward where the brain wants to go.
    // Running (scale above 1) makes hops longer, flatter and closer together.
    private Vector3 Hop(Vector3 vel, Vector3 direction, float scale, float dt)
    {
        Vector3 horiz = new Vector3(vel.X, 0f, vel.Z).MoveToward(Vector3.Zero, Def.Acceleration * 2f * dt);
        vel.X = horiz.X;
        vel.Z = horiz.Z;

        _hopTimer -= dt;   // only counts down on the ground, so the pause starts at landing
        if (direction == Vector3.Zero || _hopTimer > 0f) return vel;

        float lift = Def.HopLift * (scale > 1.01f ? Def.SprintHopLift : 1f);
        Vector3 push = direction * Def.MoveSpeed * scale;
        _hopTimer = Def.HopPause / Mathf.Max(scale, 0.1f);
        _launchTimer = 0.1f;   // off the ground cleanly, so the next frame doesn't count as landed

        float airtime = 2f * lift / Mathf.Max(-GetGravity().Y, 0.1f);
        Rpc(MethodName.PlayHop, airtime);
        return new Vector3(push.X, lift, push.Z);
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
        if (_statuses?.IsRooted ?? false) return Vector3.Zero;
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
        vel.Y = Mathf.Max(vel.Y, -Def.TerminalVelocity);
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
    // Host only. Same safe-landing rules as players; false if nowhere close is safe.
    public bool TeleportNear(Vector3 target)
    {
        if (!Multiplayer.IsServer()) return false;
        float height = Mathf.Max(Def.BodyHeight, Def.BodyRadius * 2f);
        if (SafeLanding.Find(this, target, Def.BodyRadius, height, GetRid()) is not Vector3 centre) return false;

        GlobalPosition = centre;
        Velocity = Vector3.Zero;
        return true;
    }

    // ---------- Damage and death ----------

    private void OnDamaged(float amount, Node source)
    {
        _material.AlbedoColor = Colors.White;
        _overlay.AlbedoColor = new Color(1f, 1f, 1f, 0.7f);
        Tween flash = CreateTween().SetParallel();
        flash.TweenProperty(_material, "albedo_color", BodyColor(), 0.2);
        flash.TweenProperty(_overlay, "albedo_color", OverlayColor(), 0.2);

        if (Multiplayer.IsServer()) ReactToDamage(amount, source as Node3D);   // the source is only known on the host
    }

    private void OnDied(Node source)
    {
        Rotation = new Vector3(Mathf.Pi / 2f, Rotation.Y, 0f);   // placeholder: tip over
        _telegraphing = false;
        _windingUp = -1;
        ShowBodyColor();
        _anim?.Pause();
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

    // ---------- Model and animation (every machine) ----------

    private void SetupModel()
    {
        ApplyOverlay(_model);
        _anim = FindAnimator(_model);
        if (_anim == null) return;

        if (_anim.HasAnimation(Def.IdleAnimation))
            _anim.GetAnimation(Def.IdleAnimation).LoopMode = Animation.LoopModeEnum.Linear;
        _anim.AnimationFinished += name =>
        {
            if (name.ToString() == Def.HopAnimation) PlayIdle();
        };
        PlayIdle();
    }

    private void ApplyOverlay(Node node)
    {
        if (node is GeometryInstance3D geometry) geometry.MaterialOverlay = _overlay;
        foreach (Node child in node.GetChildren()) ApplyOverlay(child);
    }

    private static AnimationPlayer FindAnimator(Node node)
    {
        if (node is AnimationPlayer player) return player;
        foreach (Node child in node.GetChildren())
            if (FindAnimator(child) is AnimationPlayer found) return found;
        return null;
    }

    private void PlayIdle()
    {
        if (_anim != null && !_health.IsDead && _anim.HasAnimation(Def.IdleAnimation))
            _anim.Play(Def.IdleAnimation, 0.15);
    }

    // The host says when each hop starts; everyone plays it stretched to fit the time in the air
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void PlayHop(float airtime)
    {
        if (_anim == null || _health.IsDead || !_anim.HasAnimation(Def.HopAnimation)) return;

        float length = (float)_anim.GetAnimation(Def.HopAnimation).Length;
        _anim.Play(Def.HopAnimation, 0.05, length / Mathf.Max(airtime + 0.1f, 0.1f));
        _anim.Seek(0, true);   // restart it even if the last hop is still playing
    }

    // Placeholder colour and model overlay together
    private void ShowBodyColor()
    {
        _material.AlbedoColor = BodyColor();
        _overlay.AlbedoColor = OverlayColor();
    }

    private Color OverlayColor()
    {
        if (_health.IsDead) return new Color(0f, 0f, 0f, 0.45f);
        return _telegraphing ? new Color(TelegraphColor, 0.5f) : Colors.Transparent;
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
