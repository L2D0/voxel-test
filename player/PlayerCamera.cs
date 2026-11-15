using Godot;

public partial class PlayerCamera : Node3D
{
    [Export] public float MouseSensitivity = 0.003f;
    [Export] public float ShoulderOffset = 0.6f;
    [Export] public float ShoulderSwitchSpeed = 10f;
    [Export] public float CameraDistance = 3f;
    [Export] public float AimCameraDistance = 1.8f;
    [Export] public float ZoomSpeed = 10f;
    [Export] public float CameraMargin = 0.3f;
    [Export] public float FirstPersonHeight = 0.7f;
    [Export] public float ThirdPersonFov = 75f;
    [Export] public float FirstPersonFov = 80f;

    public float Yaw => Rotation.Y;
    public bool FirstPerson { get; private set; }

    private PlayerController _player;
    private SpringArm3D _springArm;
    private Camera3D _camera;
    private RayCast3D _shoulderRay;
    private Node3D _model;
    private float _shoulderSide = 1f;
    private float _thirdPersonHeight;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _springArm = GetNode<SpringArm3D>("SpringArm3D");
        _camera = _springArm.GetNode<Camera3D>("Camera3D");
        _shoulderRay = GetNode<RayCast3D>("ShoulderRay");
        _model = _player.GetNode<Node3D>("Model");

        _shoulderRay.AddException(_player);
        _springArm.AddExcludedObject(_player.GetRid());
        _thirdPersonHeight = Position.Y;
        _camera.Fov = ThirdPersonFov;

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_player.MenuOpen) return;

        if (e is InputEventMouseButton mb && mb.Pressed && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Vector3 rot = Rotation;
            rot.Y -= motion.Relative.X * MouseSensitivity;
            rot.X = Mathf.Clamp(rot.X - motion.Relative.Y * MouseSensitivity,
                Mathf.DegToRad(-80f), Mathf.DegToRad(70f));
            Rotation = rot;
        }

        if (e.IsActionPressed("switch_shoulder")) _shoulderSide *= -1f;
        if (e.IsActionPressed("toggle_view")) SetFirstPerson(!FirstPerson);
    }

    private void SetFirstPerson(bool firstPerson)
    {
        FirstPerson = firstPerson;
        Position = new Vector3(0, firstPerson ? FirstPersonHeight : _thirdPersonHeight, 0);
        _camera.Fov = firstPerson ? FirstPersonFov : ThirdPersonFov;

        if (firstPerson)
        {
            _springArm.SpringLength = 0f;
            _springArm.Position = Vector3.Zero;
        }

        // Hide the body from the camera but keep its shadow
        foreach (Node child in _model.GetChildren())
            if (child is GeometryInstance3D mesh)
                mesh.CastShadow = firstPerson
                    ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
                    : GeometryInstance3D.ShadowCastingSetting.On;
    }

    public override void _Process(double delta)
    {
        if (FirstPerson) return;
        float dt = (float)delta;

        float targetX = ShoulderOffset * _shoulderSide;
        _shoulderRay.TargetPosition = new Vector3(targetX, 0, 0);
        _shoulderRay.ForceRaycastUpdate();

        float maxOffset = ShoulderOffset;
        if (_shoulderRay.IsColliding())
            maxOffset = Mathf.Max(_shoulderRay.GlobalPosition.DistanceTo(_shoulderRay.GetCollisionPoint()) - CameraMargin, 0f);

        Vector3 armPos = _springArm.Position;
        armPos.X = Mathf.Lerp(armPos.X, targetX, ShoulderSwitchSpeed * dt);
        armPos.X = _shoulderSide > 0 ? Mathf.Min(armPos.X, maxOffset) : Mathf.Max(armPos.X, -maxOffset);
        _springArm.Position = armPos;

        float targetDist = _player.IsAiming ? AimCameraDistance : CameraDistance;
        _springArm.SpringLength = Mathf.Lerp(_springArm.SpringLength, targetDist, ZoomSpeed * dt);
    }
}