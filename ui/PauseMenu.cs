using Godot;

public partial class PauseMenu : CanvasLayer
{
    private Control _main;             // the pause menu's own buttons
    private CenterContainer _settings; // the settings page, while it's open

    public override void _Ready()
    {
        Visible = false;
        GetNode<Button>("%ResumeButton").Pressed += Close;
        _main = GetNode<Control>("%ResumeButton").GetParent<Control>();

        // Draw distance moved into the shared settings page
        GetNode<Control>("%DrawDistanceSlider").Visible = false;
        GetNode<Control>("%DrawDistanceLabel").Visible = false;

        // Save world: host only, saves right now without quitting
        var resume = GetNode<Button>("%ResumeButton");
        var saveButton = new Button { Name = "SaveButton", Text = "Save world" };
        resume.GetParent().AddChild(saveButton);
        resume.GetParent().MoveChild(saveButton, resume.GetIndex() + 1);
        saveButton.Pressed += () => (GetTree().GetFirstNodeInGroup("session") as Session)?.SaveWorld();
        GetNode<Button>("%QuitButton").Pressed += () =>
        {
            GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);   // lets the host save first
            GetTree().Quit();

            var quitToMenu = new Button { Text = "Quit to menu" };
            var quit = GetNode<Button>("%QuitButton");
            quit.GetParent().AddChild(quitToMenu);
            quit.GetParent().MoveChild(quitToMenu, quit.GetIndex());   // just above Quit
            quitToMenu.Pressed += () => (GetTree().GetFirstNodeInGroup("session") as Session)?.QuitToMenu();
        };

        var settingsButton = new Button { Text = "Settings" };
        resume.GetParent().AddChild(settingsButton);
        resume.GetParent().MoveChild(settingsButton, saveButton.GetIndex() + 1);
        settingsButton.Pressed += OpenSettings;
    }

    private void OpenSettings()
    {
        _main.Visible = false;
        _settings = new CenterContainer();
        AddChild(_settings);
        _settings.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _settings.AddChild(new SettingsPanel { OnBack = CloseSettings });
    }

    private void CloseSettings()
    {
        Settings.Save();   // Back already saved; this covers leaving with Esc
        _settings?.QueueFree();
        _settings = null;
        _main.Visible = true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;

        if (_settings != null) CloseSettings();   // Esc on the settings page goes back a step
        else if (Visible) Close();
        else if (GameState.InGame) Open();   // nothing to pause before you've spawned
        GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && !Visible && GameState.InGame)
            Open();
    }

    private void Open()
    {
        Visible = true;
        if (GetNodeOrNull<Button>("%ResumeButton")?.GetParent().GetNodeOrNull<Button>("SaveButton") is Button save)
            save.Visible = Multiplayer.IsServer() && WorldSave.Current != null;   // only the host has a world to save
        GameState.MenuOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Close()
    {
        if (_settings != null) CloseSettings();
        Visible = false;
        GameState.MenuOpen = false;
        Input.MouseMode = GameState.InGame ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

}