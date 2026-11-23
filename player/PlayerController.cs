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
    [Export] public float CombatStanceTime = 3f;                            // seconds after a cast before you turn to run again
    [Export(PropertyHint.Range, "0,1")] public float BackpedalSpeedMult = 0.75f;   // speed moving straight backwards in combat
    [ExportGroup("Sprint")]
    [Export] public float SprintSpeedMult = 1.5f;
    [Export] public float SprintDrain = 20f;   // stamina per second

    [ExportGroup("Crouch")]
    [Export] public float CrouchHeight = 1.2f;
    [Export] public float CrouchSpeedMult = 0.5f;
    [Export] public float CrouchCameraDrop = 0.6f;
    [Export(PropertyHint.Range, "0,1")] public float CrouchVisibility = 0.5f;   // enemies see you at this fraction of their range

    [ExportGroup("Footsteps")]
    [Export] public float WalkNoise = 5f;
    [Export] public float SprintNoise = 12f;
    [Export] public float FootstepInterval = 0.5f;
    [ExportGroup("Death")]
    [Export] public float RespawnDelay = 6f;   // time lying where you fell: the window to be revived
    [ExportGroup("Falling")]
    [Export] public float SafeFallSpeed = 12f;      // m/s at impact; slower never hurts (about a 7 m drop)
    [Export] public float LethalFallSpeed = 30f;    // m/s at impact that deals LethalFallDamage (about a 46 m drop)
    [Export] public float LethalFallDamage = 100f;
    [Export] public float TerminalVelocity = 55f;   // the fastest anything falls; well past lethal, but stops sky islands getting silly
    [Export] public float FallDamageCurve = 1.5f;   // above 1: small overshoots barely hurt, big ones ramp up fast

    // Spells change these temporarily (ice: low friction, wind: low gravity)
    public float GravityScale = 1f;
    public float FrictionScale = 1f;
    public float AirControlScale = 1f;

    public bool IsAiming { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public float Visibility => IsCrouching ? CrouchVisibility : 1f;   // enemies' sight range scales by this
    public SkillTree Skills { get; private set; }
    public bool MenuOpen => GameState.MenuOpen || GameState.FreeCam;   // free cam: the body stands still
    public bool MenuOpen => GameState.MenuOpen;
    public bool IsStunned => _statuses?.IsStunned ?? false;
    public bool IsRooted => (_statuses?.IsRooted ?? false) || (_caster?.LocksMovement ?? false);
    public bool IsDead => _health != null && _health.IsDead;

    private PlayerCamera _camera;
    private Node3D _model;
    private Marker3D _castPoint;
    private RayCast3D _aimRay;
    private Label _speedLabel;
    private StatusEffects _statuses;
    private Health _health;
    private SpellCaster _caster;
    private Stamina _stamina;
    private CollisionShape3D _bodyShape;
    private CapsuleShape3D _capsule;
    private float _standHeight, _pivotHeight;
    private Vector3 _lastPosition;
    private bool _crouchToggled;
    public Vector3 ObservedVelocity { get; private set; }
    private float _stepDistance, _footstepTimer;
    private ModifierStack _mods;

    // A player stat after modifiers (see PlayerStat). The export is the base value.
    private float Stat(string stat, float baseValue) => _mods != null ? _mods.GetPlayer(stat, baseValue) : baseValue;
    private Vector3 _spawnPoint;
    private float _jumpBufferTimer;
    private float _coyoteTimer;
    private float _launchTimer;
    private float _fallImmuneTimer;
    private float _combatTimer;   // counts down after each cast

    // Facing the camera instead of turning to run: right after casting, while aiming, or mid-cast.
    // Taking damage doesn't count, so getting hit while running away doesn't whip you round.
    public bool InCombatStance =>
        _combatTimer > 0f || IsAiming || (_caster?.IsHolding ?? false) || (_caster?.IsPicking ?? false);
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
        _statuses = StatusEffects.Of(this);
        if (_statuses != null) _statuses.Changed += UpdateKnockdownPose;
        _health = GetNode<Health>("Health");
        _caster = GetNodeOrNull<SpellCaster>("Spellcaster");
        _stamina = GetNodeOrNull<Stamina>("Stamina");
        SetupBody();
        if (_caster != null) _caster.Casted += () => _combatTimer = CombatStanceTime;
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
    // ---------- Crouch ----------

    private void SetupBody()
    {
        _bodyShape = GetNode<CollisionShape3D>("CollisionShape3D");
        _capsule = (CapsuleShape3D)_bodyShape.Shape.Duplicate();   // every player shares the scene's shape; give this one its own
        _bodyShape.Shape = _capsule;
        _standHeight = _capsule.Height;
        _pivotHeight = _camera.Position.Y;
        _lastPosition = GlobalPosition;
    }

    // The owner decides; every machine reshapes the body so collisions and the model match
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetCrouched(bool crouched)
    {
        IsCrouching = crouched;
        float height = crouched ? Mathf.Max(CrouchHeight, _capsule.Radius * 2f) : _standHeight;
        float drop = (_standHeight - height) / 2f;   // keeps the feet where they are

        _capsule.Height = height;
        _bodyShape.Position = new Vector3(0f, -drop, 0f);
        _model.Scale = new Vector3(1f, height / _standHeight, 1f);   // placeholder squash until animations exist
        _model.Position = new Vector3(0f, -drop, 0f);

        float camera = _pivotHeight - (crouched ? CrouchCameraDrop : 0f);
        CreateTween().TweenProperty(_camera, "position:y", camera, 0.12);
    }

    // Room to stand: a full-height capsule where the player is, against terrain and extrusions
    private bool CanStand()
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = _capsule.Radius - 0.05f, Height = _standHeight - 0.1f },
            Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * 0.05f),
            CollisionMask = SpellFx.WorldLayer | SpellFx.StructureLayer,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    // ---------- Footsteps ----------

    // Host only, for every player: positions are synced, so the host can hear everyone from how fast they move.
    // Crouching is silent, walking is quiet, sprinting and bhopping carry.
    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer() || IsDead) return;

        float dt = (float)delta;
        Vector3 moved = GlobalPosition - _lastPosition;
        ObservedVelocity = moved / Mathf.Max(dt, 0.0001f);   // the host's best guess at other players' velocity
        _lastPosition = GlobalPosition;
        _stepDistance += new Vector3(moved.X, 0f, moved.Z).Length();

        _footstepTimer -= dt;
        if (_footstepTimer > 0f) return;

        float speed = _stepDistance / FootstepInterval;
        _stepDistance = 0f;
        _footstepTimer = FootstepInterval;
        if (speed > 30f) return;   // a teleport or respawn, not footsteps

        float radius = IsCrouching || speed < 1f ? 0f
                     : speed > MaxGroundSpeed * 1.2f ? SprintNoise
                     : WalkNoise;
        if (radius > 0f) Noise.Emit(this, GlobalPosition, radius, this);
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
    // Every machine: lie down while knocked down, get up when it ends
    private void UpdateKnockdownPose()
    {
        if (IsDead) return;   // death has its own pose
        float tip = (_statuses?.IsKnockedDown ?? false) ? Mathf.Pi / 2f : 0f;   // placeholder until animations exist
        _model.Rotation = new Vector3(tip, _model.Rotation.Y, 0f);
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
                                           // The spawn point may be far away and unloaded: freeze until its ground is built, then stand on it
        (GetTree().GetFirstNodeInGroup("voxel_world") as VoxelWorld)?.SetPlayer(this);
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
    // ---------- Teleporting ----------

    // Moves this player to a safe spot near target. False if nowhere close is safe.
    // Whoever calls it (the host for a spell, the player for a waystone), the player's own machine does the move.
    // ---------- Interacting and travel ----------

    private const float InteractReach = 3.5f;

    // E: use whatever's under the crosshair, if it's within reach
    private void TryInteract()
    {
        _aimRay.ForceRaycastUpdate();
        if (!_aimRay.IsColliding()) return;
        if (GlobalPosition.DistanceTo(_aimRay.GetCollisionPoint()) > InteractReach) return;
        if (_aimRay.GetCollider() is IInteractable target) target.Interact(this);
    }

    // Host to owner: go to a far-off point, like a waystone. The host may not have the ground there loaded,
    // so the owner's machine does it: move, freeze until the terrain is built, then stand on the surface.
    public void TravelTo(Vector3 point)
    {
        if (IsMultiplayerAuthority()) ApplyTravel(point);
        else RpcId(GetMultiplayerAuthority(), MethodName.ReceiveTravel, point);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTravel(Vector3 point)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host sends people places
        ApplyTravel(point);
    }

    private void ApplyTravel(Vector3 point)
    {
        GlobalPosition = point;
        Velocity = Vector3.Zero;
        (GetTree().GetFirstNodeInGroup("voxel_world") as VoxelWorld)?.SetPlayer(this);   // same as spawning there
    }
    // ---------- Loading a save ----------

    // Host to owner: put me back where I was saved, with my skills
    public void RestoreFromSave(bool hasPosition, Vector3 position, Vector3 velocity, string[] skills)
    {
        if (IsMultiplayerAuthority()) ApplySave(hasPosition, position, velocity, skills);
        else RpcId(GetMultiplayerAuthority(), MethodName.ReceiveSave, hasPosition, position, velocity, skills);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSave(bool hasPosition, Vector3 position, Vector3 velocity, string[] skills)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host restores characters
        ApplySave(hasPosition, position, velocity, skills);
    }

    private void ApplySave(bool hasPosition, Vector3 position, Vector3 velocity, string[] skills)
    {
        if (hasPosition)
        {
            GlobalPosition = position;
            Velocity = velocity;   // frozen below until the ground loads, then you carry on exactly as you were
            // Wait for the ground there to be built, but keep the exact spot: you might have saved in a cave
            (GetTree().GetFirstNodeInGroup("voxel_world") as VoxelWorld)?.SetPlayer(this, placeOnSurface: false);
        }
        Skills?.Restore(skills);

    Skills?.Restore(skills);
    }
    // Spawning and respawning: stand on the ground under the spawn point, wherever this seed put it
    public bool TryPlaceOnSurface(VoxelWorld world)
    {
        if (SafeLanding.FindSurface(this, GlobalPosition, world.TopY, world.BottomY,
                                    _capsule.Radius, _standHeight, GetRid()) is not Vector3 centre) return false;
        GlobalPosition = centre;
        Velocity = Vector3.Zero;
        GrantFallImmunity(0.5f);
        return true;
    }
    public bool TeleportNear(Vector3 target)

    {
        if (SafeLanding.Find(this, target, _capsule.Radius, _standHeight, GetRid()) is not Vector3 centre) return false;

        if (IsMultiplayerAuthority()) ApplyTeleport(centre);
        else RpcId(GetMultiplayerAuthority(), MethodName.ReceiveTeleport, centre);
        return true;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTeleport(Vector3 centre)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host moves other people
        ApplyTeleport(centre);
    }

    private void ApplyTeleport(Vector3 centre)
    {
        GlobalPosition = centre;
        Velocity = Vector3.Zero;
        _launchTimer = 0f;
        GrantFallImmunity(0.5f);   // landing a few centimetres up never hurts
    }

    // ---------- Movement ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool canAct = !MenuOpen && !IsDead && !IsStunned;
        if (IsDead) TickRespawn(dt);

        Vector2 input = canAct
            ? Input.GetVector("move_left", "move_right", "move_forward", "move_back")
            : Vector2.Zero;
        if (IsRooted) input = Vector2.Zero;
        float inputStrength = Mathf.Min(input.Length(), 1f);
        Vector3 wishDir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, _camera.Yaw).Normalized();

        IsAiming = canAct && Input.IsActionPressed("aim") && !(_caster?.AimReplaced ?? false);   // alt-fire spells own right click
                                                                                                 // Crouch: hold to stay down. Standing back up waits for headroom.
                                                                                                 // Hold C, or press to toggle if that's set. Sprinting cancels a toggled crouch.
        if (!Settings.CrouchToggle) _crouchToggled = false;
        else if (canAct && Input.IsActionJustPressed("crouch")) _crouchToggled = !_crouchToggled;
        if (Input.IsActionJustPressed("sprint")) _crouchToggled = false;
        bool wantCrouch = Settings.CrouchToggle ? _crouchToggled : canAct && Input.IsActionPressed("crouch");
        if (wantCrouch != IsCrouching && (wantCrouch || CanStand())) Rpc(MethodName.SetCrouched, wantCrouch);
        // Sprint: pressing it breaks combat stance, so it's how you disengage. Casting again ends it.
        if (canAct && Input.IsActionJustPressed("sprint")) _combatTimer = 0f;
        IsSprinting = canAct && Input.IsActionPressed("sprint") && inputStrength > 0.1f && !InCombatStance
                      && IsOnFloor() && !IsCrouching && (_stamina?.CanSprint ?? true); if (IsSprinting) _stamina?.Drain(SprintDrain * dt);
        float maxSpeed = Stat(PlayerStat.MoveSpeed, MaxGroundSpeed) * (IsAiming ? AimSpeedMult : 1f);
        if (InCombatStance) maxSpeed *= Mathf.Lerp(1f, BackpedalSpeedMult, Mathf.Clamp(input.Y, 0f, 1f));   // input.Y > 0 is backwards
        if (IsSprinting) maxSpeed *= SprintSpeedMult;
                if (IsCrouching) maxSpeed *= CrouchSpeedMult;
        maxSpeed *= 1f - (_statuses?.Slow ?? 0f);
        float wishSpeed = maxSpeed * inputStrength;

        _launchTimer -= dt;
        _fallImmuneTimer -= dt;
        _combatTimer -= dt;
        bool grounded = IsOnFloor() && _launchTimer <= 0f;

        // Jump buffer and coyote time
        _jumpBufferTimer -= dt;
        if (canAct && !IsRooted && Input.IsActionJustPressed("jump")) _jumpBufferTimer = JumpBuffer; if (grounded) _coyoteTimer = CoyoteTime;
        else _coyoteTimer -= dt;

        Vector3 vel = Velocity;
        Vector3 horiz = new Vector3(vel.X, 0, vel.Z);

        if (_jumpBufferTimer > 0f && _coyoteTimer > 0f && !(_caster?.LocksJump ?? false))
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
                        vel.Y = Mathf.Max(vel.Y, -TerminalVelocity);
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
        if (IsDead || IsStunned) return;   // lying where they fell, or stunned
        Vector3 rot = _model.Rotation;

        if (_camera.FirstPerson || (_caster?.LocksFacing ?? false))
            rot.Y = _camera.Yaw;
        else if (InCombatStance)   // aiming, holding a cast, or just cast: face the camera, strafe and backpedal
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
        if (e.IsActionPressed("interact") && IsMultiplayerAuthority() && !IsDead) TryInteract();
    }

    // ---------- Debug (the F1 panel calls these) ----------

    // Health only changes on the host, so a client asks for it
    public void DebugShield()
    {
        if (Multiplayer.IsServer()) _health.AddShield(this, 30f, 6f);
        else RpcId(1, MethodName.ReceiveDebug, 0);
    }

    public void DebugHeal()
    {
        if (Multiplayer.IsServer()) _health.SetCurrent(_health.Max);
        else RpcId(1, MethodName.ReceiveDebug, 1);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveDebug(int what)
    {
        if (!Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != GetMultiplayerAuthority()) return;
        if (what == 0) _health.AddShield(this, 30f, 6f);
        else _health.SetCurrent(_health.Max);
    }

    public void DebugRefill()
    {
        GetNodeOrNull<Mana>("Mana")?.Restore(9999f);
        _stamina?.Restore(9999f);
    }

    public void DebugTeleportToCrosshair()
    {
        _aimRay.ForceRaycastUpdate();
        if (_aimRay.IsColliding()) TeleportNear(_aimRay.GetCollisionPoint());
    }

    public bool DebugModifiersOn => _mods?.DebugOn ?? false;
    public void ToggleDebugModifiers() => _mods?.ToggleDebug();

    private void BuildTerrain()
    {
        _aimRay.ForceRaycastUpdate();
        if (!_aimRay.IsColliding()) return;

        if (_aimRay.GetCollider() is Node hit && hit.GetParent() is VoxelChunk chunk)
            chunk.Fill(_aimRay.GetCollisionPoint(), 1.5f);
    }
}
