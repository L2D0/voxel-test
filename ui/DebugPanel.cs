using Godot;

// F1: the debug panel. Every debug tool in one place, plus a free camera.
//
// Free cam: the camera comes off the player and flies on its own. The player's body stays where it is and
// carries on as normal (enemies can see it and attack it). Chunk streaming stays centred on the player, so
// the camera never loads terrain; it's held inside render distance instead. It passes through terrain.
// Controls while flying (panel closed): mouse to look, WASD to move, Space up, Ctrl down, Shift faster.
public partial class DebugPanel : CanvasLayer
{
    private PanelContainer _panel;
    private Button _freeCamButton, _modsButton, _friendlyFireButton, _saveButton;
    private Button _spawnA, _spawnB;
    private Label _hostOnly;

    private Camera3D _freeCam;
    private Camera3D _playerCam;
    private float _yaw, _pitch;
    private float _speed = 15f;
    private const float LookSensitivity = 0.003f;

    public override void _Ready()
    {
        Layer = 20;
        Build();
        _panel.Visible = false;
    }

    // ---------- Opening and closing ----------

    public override void _Input(InputEvent e)
    {
        if (!GameState.InGame) return;

        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 })
        {
            SetOpen(!_panel.Visible);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_panel.Visible && e.IsActionPressed("ui_cancel"))   // Esc closes the panel before the pause menu sees it
        {
            SetOpen(false);
            GetViewport().SetInputAsHandled();
        }
    }

    private void SetOpen(bool open)
    {
        _panel.Visible = open;
        GameState.MenuOpen = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (open) Refresh();
    }

    // ---------- The panel ----------

    private void Build()
    {
        _panel = new PanelContainer();
        AddChild(_panel);
        _panel.Position = new Vector2(16f, 140f);   // under the HUD's bars

        var list = new VBoxContainer { CustomMinimumSize = new Vector2(260f, 0f) };
        _panel.AddChild(list);
        list.AddChild(new Label { Text = "Debug  (F1)" });

        Section(list, "Camera");
        _freeCamButton = Add(list, "", ToggleFreeCam);
        var speedLabel = new Label { Text = $"Free cam speed: {_speed:0}" };
        var speed = new HSlider { MinValue = 3, MaxValue = 80, Step = 1, Value = _speed };
        speed.ValueChanged += v => { _speed = (float)v; speedLabel.Text = $"Free cam speed: {_speed:0}"; };
        list.AddChild(speedLabel);
        list.AddChild(speed);

        Section(list, "Player");
        Add(list, "Shield +30 for 6 s", () => Me()?.DebugShield());
        Add(list, "Heal to full", () => Me()?.DebugHeal());
        Add(list, "Refill mana and stamina", () => Me()?.DebugRefill());
        Add(list, "Teleport to crosshair", () => Me()?.DebugTeleportToCrosshair());
        Add(list, "Give 5 of every item", () => Inventory.Of(Me())?.DebugGiveAll());
        _modsButton = Add(list, "", () => { Me()?.ToggleDebugModifiers(); Refresh(); });

        Section(list, "Enemies");
        _spawnA = Add(list, "", () => EnemySpawner.Of(this)?.DebugSpawn(0));
        _spawnB = Add(list, "", () => EnemySpawner.Of(this)?.DebugSpawn(1));

        Section(list, "World");
        _friendlyFireButton = Add(list, "", () => { (GetTree().GetFirstNodeInGroup("session") as Session)?.ToggleFriendlyFire(); Refresh(); });
        _saveButton = Add(list, "Save world", () => (GetTree().GetFirstNodeInGroup("session") as Session)?.SaveWorld());
        _hostOnly = new Label { Text = "World tools are host only" };
        list.AddChild(_hostOnly);
    }

    private void Refresh()
    {
        _freeCamButton.Text = $"Free cam: {(_freeCam != null ? "on" : "off")}";
        _modsButton.Text = $"Debug modifiers: {((Me()?.DebugModifiersOn ?? false) ? "on" : "off")}";
        _friendlyFireButton.Text = $"Friendly fire: {(Teams.FriendlyFire ? "on" : "off")}";

        EnemySpawner spawner = EnemySpawner.Of(this);
        _spawnA.Text = $"Spawn {spawner?.DebugName(0) ?? "-"}";
        _spawnB.Text = $"Spawn {spawner?.DebugName(1) ?? "-"}";

        bool host = Multiplayer.IsServer();
        _friendlyFireButton.Disabled = !host;
        _saveButton.Disabled = !host || WorldSave.Current == null;
        _hostOnly.Visible = !host;
    }

    private static void Section(VBoxContainer list, string title)
    {
        list.AddChild(new HSeparator());
        list.AddChild(new Label { Text = title });
    }

    private static Button Add(VBoxContainer list, string text, System.Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        list.AddChild(button);
        return button;
    }

    // This machine's own player
    private PlayerController Me()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is PlayerController player && player.IsMultiplayerAuthority()) return player;
        return null;
    }

    // ---------- Free cam ----------

    private void ToggleFreeCam()
    {
        if (_freeCam == null) StartFreeCam();
        else StopFreeCam();
        Refresh();
    }

    private void StartFreeCam()
    {
        _playerCam = GetViewport().GetCamera3D();
        if (_playerCam == null) return;

        _freeCam = new Camera3D { Fov = _playerCam.Fov, Far = _playerCam.Far };
        GetTree().CurrentScene.AddChild(_freeCam);
        _freeCam.GlobalTransform = _playerCam.GlobalTransform;
        _freeCam.MakeCurrent();

        Vector3 euler = _freeCam.GlobalBasis.GetEuler();
        _pitch = euler.X;
        _yaw = euler.Y;
        GameState.FreeCam = true;
    }

    private void StopFreeCam()
    {
        if (IsInstanceValid(_playerCam)) _playerCam.MakeCurrent();
        _freeCam?.QueueFree();
        _freeCam = null;
        GameState.FreeCam = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_freeCam == null || _panel.Visible) return;

        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;   // click back in after the pause menu
            return;
        }
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * LookSensitivity * Settings.MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * LookSensitivity * Settings.MouseSensitivity,
                                 -Mathf.Pi / 2f + 0.01f, Mathf.Pi / 2f - 0.01f);
            _freeCam.Rotation = new Vector3(_pitch, _yaw, 0f);
        }
    }

    public override void _Process(double delta)
    {
        if (_freeCam == null) return;
        if (!IsInstanceValid(_playerCam)) { StopFreeCam(); return; }   // the player left or the scene changed
        if (_panel.Visible || GameState.MenuOpen) return;

        Vector3 move = Vector3.Zero;
        Basis basis = _freeCam.GlobalBasis;
        if (Input.IsPhysicalKeyPressed(Key.W)) move -= basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.S)) move += basis.Z;
        if (Input.IsPhysicalKeyPressed(Key.A)) move -= basis.X;
        if (Input.IsPhysicalKeyPressed(Key.D)) move += basis.X;
        if (Input.IsPhysicalKeyPressed(Key.Space)) move += Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.Ctrl)) move += Vector3.Down;

        float speed = _speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? 3f : 1f);
        Vector3 position = _freeCam.GlobalPosition + move.Normalized() * speed * (float)delta;
        _freeCam.GlobalPosition = Clamp(position);
    }

    // Inside render distance of the player, and inside the world's height
    private Vector3 Clamp(Vector3 position)
    {
        if (Me() is PlayerController player)
        {
            float reach = Settings.DrawDistance * VoxelChunk.Size;
            Vector3 flat = new(position.X - player.GlobalPosition.X, 0f, position.Z - player.GlobalPosition.Z);
            if (flat.Length() > reach)
            {
                flat = flat.Normalized() * reach;
                position.X = player.GlobalPosition.X + flat.X;
                position.Z = player.GlobalPosition.Z + flat.Z;
            }
        }
        if (GetTree().GetFirstNodeInGroup("voxel_world") is VoxelWorld world)
            position.Y = Mathf.Clamp(position.Y, world.BottomY, world.TopY + 64f);
        return position;
    }
}
