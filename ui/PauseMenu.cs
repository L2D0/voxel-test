using Godot;

public partial class PauseMenu : CanvasLayer
{
    private Label _drawDistanceLabel;

    public override void _Ready()
    {
        Visible = false;
        _drawDistanceLabel = GetNode<Label>("%DrawDistanceLabel");

        GetNode<Button>("%ResumeButton").Pressed += Close;

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
        };

        var slider = GetNode<HSlider>("%DrawDistanceSlider");
        slider.Value = Settings.DrawDistance;
        slider.ValueChanged += value =>
        {
            Settings.DrawDistance = (int)value;
            UpdateLabel();
        };

        UpdateLabel();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;

        if (Visible) Close();
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
        Visible = false;
        GameState.MenuOpen = false;
        Input.MouseMode = GameState.InGame ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    private void UpdateLabel() => _drawDistanceLabel.Text = $"Draw distance: {Settings.DrawDistance} chunks";
}