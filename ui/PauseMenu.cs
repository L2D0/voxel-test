using Godot;

public partial class PauseMenu : CanvasLayer
{
    private Label _drawDistanceLabel;

    public override void _Ready()
    {
        Visible = false;
        _drawDistanceLabel = GetNode<Label>("%DrawDistanceLabel");

        GetNode<Button>("%ResumeButton").Pressed += Close;
        GetNode<Button>("%QuitButton").Pressed += () => GetTree().Quit();

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