using Godot;

public partial class PlayerController : CharacterBody3D
{
    [ExportGroup("Ground")]
    [Export] public float MaxGroundSpeed = 6f;
    [Export] public float GroundAccel = 10f;
    [Export] public float Friction = 6f;
    [Export] public float StopSpeed = 2f;
    [Export] public float AimSpeedMult = 0.6f;

    [ExportGroup("Air")]
    [Export] public float AirAccel = 10f;
    [Export] public float AirWishSpeedCap = 0.6f;
    [Export] public float MaxAirSpeed = 9f;

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
    private Node _gameState;
    private PackedScene _fireballScene;

    private float _jumpBufferTimer;
    private float _coyoteTimer;

    public override void _Ready()
    {
        _camera = GetNode<PlayerCamera>("CameraPivot");
        _model = GetNode<Node3D>("Model");
        _castPoint = GetNode<Marker3D>("Model/CastPoint");
        _aimRay = GetNode<RayCast3D>("CameraPivot/SpringArm3D/Camera3D/AimRay");
        _speedLabel = GetNodeOrNull<Label>("%SpeedLabel");
        _gameState = GetNode("/root/GameState");
        _fireballScene = GD.Load<PackedScene>("res://spells/fireball.tscn");

        _aimRay.AddException(this);
    }

    public bool MenuOpen => _gameState.Get("menu_open").AsBool();

    public void AddImpulse(Vector3 impulse) => Velocity += impulse;

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

        // Jump buffer and coyote time
        _jumpBufferTimer -= dt;
        if (canAct && Input.IsActionJustPressed("jump")) _jumpBufferTimer = JumpBuffer;
        if (IsOnFloor()) _coyoteTimer = CoyoteTime;
        else _coyoteTimer -= dt;

        Vector3 vel = Velocity;
        Vector3 horiz = new Vector3(vel.X, 0, vel.Z);
        bool grounded = IsOnFloor();

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
        {
            // Jumping on the landing frame skips friction (that's the bhop), but trims excess speed
            float speed = horiz.Length();
            if (speed > maxSpeed)
                horiz = horiz.Normalized() * (maxSpeed + (speed - maxSpeed) * HopSpeedRetention);

            vel.Y = JumpVelocity;
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
            horiz = AirAccelerate(horiz, wishDir, wishSpeed, dt);
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

    // ---------- Actions (temporary, these move into the spell framework next) ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (MenuOpen) return;

        if (e.IsActionPressed("cast")) Cast();
        if (e.IsActionPressed("build")) BuildTerrain();
    }

    private void Cast()
    {
        _model.Rotation = new Vector3(0, _camera.Yaw, 0);

        _aimRay.ForceRaycastUpdate();
        Vector3 target = _aimRay.IsColliding()
            ? _aimRay.GetCollisionPoint()
            : _aimRay.ToGlobal(_aimRay.TargetPosition);

        var fireball = _fireballScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(fireball);
        fireball.GlobalPosition = _castPoint.GlobalPosition;
        fireball.Set("direction", (target - fireball.GlobalPosition).Normalized());
        fireball.Set("caster", this);
    }

    private void BuildTerrain()
    {
        _aimRay.ForceRaycastUpdate();
        if (!_aimRay.IsColliding()) return;

        if (_aimRay.GetCollider() is Node hit && hit.GetParent() is VoxelChunk chunk)
            chunk.Fill(_aimRay.GetCollisionPoint(), 1.5f);
    }
}