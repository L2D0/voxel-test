using Godot;

public partial class PlayerController : CharacterBody3D, IImpulseReceiver, ICaster
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
    [ExportGroup("Death")]
    [Export] public float RespawnDelay = 6f;   // time lying where you fell: the window to be revived
    [ExportGroup("Falling")]
    [Export] public float SafeFallSpeed = 12f;      // m/s at impact; slower never hurts (about a 7 m drop)
    [Export] public float LethalFallSpeed = 30f;    // m/s at impact that deals LethalFallDamage (about a 46 m drop)
    [Export] public float LethalFallDamage = 100f;
    [Export] public float FallDamageCurve = 1.5f;   // above 1: small overshoots barely hurt, big ones ramp up fast

    // Spells change these temporarily (ice: low friction, wind: low gravity)
    public float GravityScale = 1f;
    public float FrictionScale = 1f;
    public float AirControlScale = 1f;

    public bool IsAiming { get; private set; }
    public SkillTree Skills { get; private set; }
    public bool MenuOpen => GameState.MenuOpen;
    public bool IsDead => _health != null && _health.IsDead;

    private PlayerCamera _camera;
    private Node3D _model;
    private Marker3D _castPoint;
    private RayCast3D _aimRay;
    private Label _speedLabel;
    private Health _health;
    private SpellCaster _caster; 
    private ModifierStack _mods;

    // A player stat after modifiers (see PlayerStat). The export is the base value.
    private float Stat(string stat, float baseValue) => _mods != null ? _mods.GetPlayer(stat, baseValue) : baseValue;
    private Vector3 _spawnPoint;
    private float _jumpBufferTimer;
    private float _coyoteTimer;
    private float _launchTimer; 
    private float _fallImmuneTimer;
    private float _respawnTimer;

    // ---------- Setup ----------

    public override void _EnterTree()
    {
        // Runs before _Ready: the peer named in this node's name controls it
        SetMultiplayerAuthority(int.Parse(Name));
    }

    public override void _Ready()
    {
        _camera = GetNode<PlayerCamera>("CameraPivot");
        _model = GetNode<Node3D>("Model");
        _castPoint = GetNode<Marker3D>("Model/CastPoint");
        _aimRay = GetNode<RayCast3D>("CameraPivot/SpringArm3D/Camera3D/AimRay");
        _speedLabel = GetNodeOrNull<Label>("%SpeedLabel");
        _health = GetNode<Health>("Health");
        _caster = GetNodeOrNull<SpellCaster>("Spellcaster");
        _mods = GetNode<ModifierStack>("Modifiers");
        Skills = GetNodeOrNull<SkillTree>("SkillTree");

        _aimRay.AddException(this); _aimRay.CollisionMask = 1u | (1u << 1) | (1u << 2);   // World, Characters, Structures
        _spawnPoint = GlobalPosition;
        AddToGroup("players");
            
        _health.Died += OnDied;
        _health.Revived += OnRevived;

        if (IsMultiplayerAuthority())
        {
            GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D").MakeCurrent();
            GameState.InGame = true;
            (GetTree().GetFirstNodeInGroup("voxel_world") as VoxelWorld)?.SetPlayer(this);
        }
        else
        {
            DisableRemote();
        }
    }

    // Another machine controls this player: we only display it
    private void DisableRemote()
    {
        SetPhysicsProcess(false);
        SetProcessUnhandledInput(false);

        _camera.SetProcess(false);
        _camera.SetProcessUnhandledInput(false);
        GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D").Current = false;

        foreach (string path in new[] { "Modifiers", "GameMenu", "CanvasLayer/SpellWheel", "CanvasLayer/HUD" })
        {
            Node node = GetNodeOrNull(path);
            if (node == null) continue;
            node.SetProcess(false);
            node.SetProcessInput(false);
            node.SetProcessUnhandledInput(false);
        }

        GetNode<CanvasLayer>("CanvasLayer").Visible = false;
        if (GetNodeOrNull("GameMenu") is CanvasLayer menu) menu.Visible = false;
    }

    // ---------- Knockback ----------

    public void AddImpulse(Vector3 impulse)
    {
        if (IsMultiplayerAuthority()) ApplyImpulse(impulse);
        else RpcId(GetMultiplayerAuthority(), MethodName.ReceiveImpulse, impulse);   // their machine moves them
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveImpulse(Vector3 impulse)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host sends knockback
        ApplyImpulse(impulse);
    }

    private void ApplyImpulse(Vector3 impulse)
    {
        Velocity += impulse * EntityInfo.KnockbackScale(this);
        _launchTimer = 0.15f;   // ignore the ground briefly so friction can't eat the launch
    }

    // ---------- Health ----------

    // Dying leaves your body where it fell for RespawnDelay seconds, long enough for someone
    // to revive you. If nobody does, you come back at the spawn point.
    private void OnDied(Node source)
    {
        _model.Rotation = new Vector3(Mathf.Pi / 2f, _model.Rotation.Y, 0f);   // placeholder: tip over
        if (IsMultiplayerAuthority()) _respawnTimer = RespawnDelay;
    }

    // Revived on the spot, or restored at spawn: either way, stand back up
    private void OnRevived()
    {
        _model.Rotation = new Vector3(0f, _model.Rotation.Y, 0f);
        _respawnTimer = 0f;
    }

    // Owner only, while dead
    private void TickRespawn(float dt)
    {
        _respawnTimer -= dt;
        if (_respawnTimer > 0f) return;

        GlobalPosition = _spawnPoint;      // my machine moves me
        Velocity = Vector3.Zero;
        _respawnTimer = float.MaxValue;    // don't ask twice while the host's answer is on its way
        _health.RequestRespawn();          // the host restores my health
    }

    // ---------- Falling ----------

    // Hitting the ground faster than SafeFallSpeed hurts, rising to LethalFallDamage at LethalFallSpeed.
    // It's the speed at impact, not the height fallen, so anything that slows you first protects you.
    private void Land(float speed)
    {
        float safe = Stat(PlayerStat.SafeFallSpeed, SafeFallSpeed);
        if (speed <= safe || _fallImmuneTimer > 0f) return;
        if (Skills != null && Skills.HasFlag("FallDamageImmune")) return;   // Wind's early node

        float t = Mathf.Clamp((speed - safe) / (LethalFallSpeed - safe), 0f, 1f);
        float damage = LethalFallDamage * Mathf.Pow(t, FallDamageCurve) * Stat(PlayerStat.FallDamage, 1f);
        _health.TakeOwnDamage(damage, DamageType.Physical);
    }

    // Spells that set you down gently (Tornado's drop, a Bubble popping) call this
    public void GrantFallImmunity(float seconds) => _fallImmuneTimer = Mathf.Max(_fallImmuneTimer, seconds);

    // ---------- Movement ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool canAct = !MenuOpen && !IsDead;
        if (IsDead) TickRespawn(dt);

        Vector2 input = canAct
            ? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
            : Vector2.Zero;
        float inputStrength = Mathf.Min(input.Length(), 1f);
        Vector3 wishDir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, _camera.Yaw).Normalized();

        IsAiming = canAct && Input.IsActionPressed("aim") && !(_caster?.AimReplaced ?? false);   // alt-fire spells own right click
        float maxSpeed = Stat(PlayerStat.MoveSpeed, MaxGroundSpeed) * (IsAiming ? AimSpeedMult : 1f);
        float wishSpeed = maxSpeed * inputStrength;

        _launchTimer -= dt;
        _fallImmuneTimer -= dt;
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
            vel.Y = Mathf.Max(vel.Y, 0f) + Stat(PlayerStat.JumpVelocity, JumpVelocity);
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
        bool wasAirborne = !IsOnFloor();          // still last frame's answer at this point
        float fallSpeed = Mathf.Max(-vel.Y, 0f);  // speed going down, before the landing zeroes it
        MoveAndSlide();
        if (wasAirborne && IsOnFloor()) Land(fallSpeed);

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
        float limit = Mathf.Max(before, Stat(PlayerStat.AirSpeed, MaxAirSpeed));
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
        if (IsDead) return;   // lying where they fell
        Vector3 rot = _model.Rotation;

        if (_camera.FirstPerson || (_caster?.LocksFacing ?? false))
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

    // Where the player is looking right now. Works on every machine, since the camera pivot's rotation is synced.
    public Vector3 LiveAimDirection => -_camera.GlobalBasis.Z;
    public Vector3 LiveCastOrigin => _castPoint.GlobalPosition;

    // The crosshair's line: from the camera, straight through the middle of the screen
    public Vector3 AimFrom => _aimRay.GlobalPosition;
    public Vector3 AimForward => (_aimRay.ToGlobal(_aimRay.TargetPosition) - _aimRay.GlobalPosition).Normalized();

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

        // F2: 30 shield for 6 seconds. Host window only, since only the host changes health.
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 }) _health.AddShield(this, 30f, 6f);
    }

    private void BuildTerrain()
    {
        _aimRay.ForceRaycastUpdate();
        if (!_aimRay.IsColliding()) return;

        if (_aimRay.GetCollider() is Node hit && hit.GetParent() is VoxelChunk chunk)
            chunk.Fill(_aimRay.GetCollisionPoint(), 1.5f);
    }
}
