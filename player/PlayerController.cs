using Godot;

public partial class PlayerController : CharacterBody3D, IImpulseReceiver
{
    [ExportGroup("Ground")]
    [Export] public float MaxGroundSpeed = 6f;
    [Export] public float GroundAccel = 10f;
    [Export] public float Friction = 6f;
    [Export] public float StopSpeed = 2f;
    [Export] public float AimSpeedMult = 0.6f;

    [ExportGroup("Air")]
    [Export] public float AirAccel = 14f;
    [Export] public float AirWishSpeedCap = 1f;
    [Export] public float MaxAirSpeed = 9f;
    [Export] public float AirSteer = 4f;        // W-only steering strength (turns, keeps speed)
    [Export] public float AirMoveSpeed = 3.5f;  // below this speed you get normal-ish air control
    [Export] public float AirMoveAccel = 4f;

    [ExportGroup("Jump")]
    [Export] public float JumpVelocity = 5f;
    [Export] public float JumpBuffer = 0.12f;
    [Export] public float CoyoteTime = 0.1f;
    [Export(PropertyHint.Range, "0,1")] public float HopSpeedRetention = 0.85f;

    [ExportGroup("Facing")]
    [Export] public float TurnSpeed = 10f;

    // Spells change these temporarily (ice: low friction, wind: low gravity)
    public float GravityScale = 1f;
    public float FrictionScale = 1f;
    public float AirControlScale = 1f;

    public bool IsAiming { get; private set; }

    private PlayerCamera _camera;
    private Node3D _model;
    private Marker3D _castPoint;
    private RayCast3D _aimRay;
    private Label _speedLabel;
    private Label _healthLabel;
    private Node _gameState;
    private Health _health;

    private Vector3 _spawnPoint;
    private float _jumpBufferTimer;
    private float _coyoteTimer;
    private float _launchTimer;

    public override void _Ready()
    {
        _camera = GetNode<PlayerCamera>("CameraPivot");
        _model = GetNode<Node3D>("Model");
        _castPoint = GetNode<Marker3D>("Model/CastPoint");
        _aimRay = GetNode<RayCast3D>("CameraPivot/SpringArm3D/Camera3D/AimRay");
        _speedLabel = GetNodeOrNull<Label>("%SpeedLabel");
        _healthLabel = GetNodeOrNull<Label>("%HealthLabel");
        _gameState = GetNode("/root/GameState");
        _health = GetNode<Health>("Health");

        _aimRay.AddException(this);
        _spawnPoint = GlobalPosition;

        _health.Changed += (current, max) => UpdateHealthLabel();
        _health.Died += OnDied;
        UpdateHealthLabel();
    }

    public bool MenuOpen => _gameState.Get("menu_open").AsBool();

    public void AddImpulse(Vector3 impulse)
    {
        Velocity += impulse;
        _launchTimer = 0.15f;   // ignore the ground briefly so friction can't eat the launch
    }

    // ---------- Health ----------

    private void UpdateHealthLabel()
    {
        if (_healthLabel != null)
            _healthLabel.Text = $"HP {_health.Current:0} / {_health.MaxHealth:0}";
    }

    private void OnDied(Node source)
    {
        GlobalPosition = _spawnPoint;
        Velocity = Vector3.Zero;
        _health.Reset();
    }

    // ---------- Movement ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool canAct = !MenuOpen;

        Vector2 input = canAct
            ? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
            : Vector2.Zero;
        float inputStrength = Mathf.Min(input.Length(), 1f);
        Vector3 wishDir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, _camera.Yaw).Normalized();

        IsAiming = canAct && Input.IsActionPressed("aim");
        float maxSpeed = MaxGroundSpeed * (IsAiming ? AimSpeedMult : 1f);
        float wishSpeed = maxSpeed * inputStrength;

        _launchTimer -= dt;
        bool grounded = IsOnFloor() && _launchTimer <= 0f;

        // Jump buffer and coyote time
        _jumpBufferTimer -= dt;
        if (canAct && Input.IsActionJustPressed("jump")) _jumpBufferTimer = JumpBuffer;
        if (grounded) _coyoteTimer = CoyoteTime;
        else _coyoteTimer -= dt;

        Vector3 vel = Velocity;
        Vector3 horiz = new Vector3(vel.X, 0, vel.Z);

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            // Jumping on the landing frame skips friction (that's the bhop), but trims excess speed
            float speed = horiz.Length();
            if (speed > maxSpeed)
                horiz = horiz.Normalized() * (maxSpeed + (speed - maxSpeed) * HopSpeedRetention);

            // Adds to upward momentum (rising pillar, updrafts), ignores downward
            vel.Y = Mathf.Max(vel.Y, 0f) + JumpVelocity;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            grounded = false;
        }

        if (grounded)
        {
            horiz = ApplyFriction(horiz, dt);
            horiz = Accelerate(horiz, wishDir, wishSpeed, GroundAccel, dt);
        }
        else
        {
            // Low speed: near-normal control, for small hops and platforming
            if (horiz.Length() < AirMoveSpeed)
                horiz = Accelerate(horiz, wishDir, Mathf.Min(wishSpeed, AirMoveSpeed), AirMoveAccel * AirControlScale, dt);

            // Strafe gains
            horiz = AirAccelerate(horiz, wishDir, wishSpeed, dt);

            // W only, no strafe keys: steer toward the camera without changing speed
            if (Mathf.Abs(input.X) < 0.01f && input.Y < 0f)
                horiz = ApplyAirSteer(horiz, wishDir, dt);

            vel += GetGravity() * GravityScale * dt;
        }

        vel.X = horiz.X;
        vel.Z = horiz.Z;
        Velocity = vel;
        MoveAndSlide();

        UpdateFacing(wishDir, dt);

        if (_speedLabel != null)
            _speedLabel.Text = $"{new Vector2(Velocity.X, Velocity.Z).Length():0.0} m/s";
    }

    private Vector3 ApplyFriction(Vector3 v, float dt)
    {
        float speed = v.Length();
        if (speed < 0.01f) return Vector3.Zero;

        float control = Mathf.Max(speed, StopSpeed);
        float drop = control * Friction * FrictionScale * dt;
        float newSpeed = Mathf.Max(speed - drop, 0f);
        return v * (newSpeed / speed);
    }

    private static Vector3 Accelerate(Vector3 v, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float current = v.Dot(wishDir);
        float add = wishSpeed - current;
        if (add <= 0f) return v;

        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, add);
        return v + wishDir * accelSpeed;
    }

    private Vector3 AirAccelerate(Vector3 v, Vector3 wishDir, float wishSpeed, float dt)
    {
        if (wishDir == Vector3.Zero) return v;

        float before = v.Length();
        float current = v.Dot(wishDir);
        float add = Mathf.Min(wishSpeed, AirWishSpeedCap) - current;
        if (add <= 0f) return v;

        float accelSpeed = Mathf.Min(AirAccel * AirControlScale * wishSpeed * dt, add);
        Vector3 result = v + wishDir * accelSpeed;

        // Input can steer at any speed, but only add speed up to MaxAirSpeed
        float limit = Mathf.Max(before, MaxAirSpeed);
        if (result.Length() > limit) result = result.Normalized() * limit;
        return result;
    }

    private Vector3 ApplyAirSteer(Vector3 v, Vector3 wishDir, float dt)
    {
        float speed = v.Length();
        if (speed < 0.1f) return v;

        Vector3 dir = v / speed;
        float dot = dir.Dot(wishDir);
        if (dot <= 0f) return v;   // only steers toward where you're already roughly heading

        dir = (dir + wishDir * AirSteer * AirControlScale * dot * dot * dt).Normalized();
        return dir * speed;
    }

    private void UpdateFacing(Vector3 wishDir, float dt)
    {
        Vector3 rot = _model.Rotation;

        if (_camera.FirstPerson)
            rot.Y = _camera.Yaw;
        else if (IsAiming)
            rot.Y = Mathf.LerpAngle(rot.Y, _camera.Yaw, TurnSpeed * dt);
        else if (wishDir != Vector3.Zero)
            rot.Y = Mathf.LerpAngle(rot.Y, Mathf.Atan2(-wishDir.X, -wishDir.Z), TurnSpeed * dt);
        else
            return;

        _model.Rotation = rot;
    }

    // ---------- Spells ----------

    public SpellContext BuildSpellContext()
    {
        _model.Rotation = new Vector3(0, _camera.Yaw, 0);   // face where you're casting

        _aimRay.ForceRaycastUpdate();
        bool hit = _aimRay.IsColliding();
        Vector3 point = hit ? _aimRay.GetCollisionPoint() : _aimRay.ToGlobal(_aimRay.TargetPosition);
        Vector3 origin = _castPoint.GlobalPosition;

        return new SpellContext
        {
            Caster = this,
            CastOrigin = origin,
            AimPoint = point,
            AimNormal = hit ? _aimRay.GetCollisionNormal() : Vector3.Up,
            AimDirection = (point - origin).Normalized(),
            AimCollider = hit ? _aimRay.GetCollider() : null,
            AimHit = hit,
        };
    }

    // ---------- Debug actions ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (MenuOpen) return;
        if (e.IsActionPressed("build")) BuildTerrain();
    }

    private void BuildTerrain()
    {
        _aimRay.ForceRaycastUpdate();
        if (!_aimRay.IsColliding()) return;

        if (_aimRay.GetCollider() is Node hit && hit.GetParent() is VoxelChunk chunk)
            chunk.Fill(_aimRay.GetCollisionPoint(), 1.5f);
    }
}