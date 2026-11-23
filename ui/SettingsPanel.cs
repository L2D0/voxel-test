using Godot;

// The settings page, built in code. Used by both the main menu and the pause menu.
// Changes apply straight away; Back saves them to disk and calls OnBack.
public partial class SettingsPanel : VBoxContainer
{
    public System.Action OnBack;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(360f, 0f);
        AddChild(new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center });

        AddSlider("Draw distance", 2, 12, 1, Settings.DrawDistance, v => Settings.DrawDistance = (int)v, v => $"{v:0} chunks");
        AddSlider("Field of view", 60, 100, 1, Settings.Fov, v => Settings.SetFov((float)v), v => $"{v:0}°");
        AddSlider("Mouse sensitivity", 0.1, 3, 0.05, Settings.MouseSensitivity, v => Settings.SetMouseSensitivity((float)v), v => $"{v:0.00}×");
        AddToggle("Shadows", Settings.Shadows, on =>
        {
            Settings.SetShadows(on);
            Settings.ApplyShadows(GetTree().Root);
        });
        AddToggle("Crouch: press to toggle (off: hold)", Settings.CrouchToggle, Settings.SetCrouchToggle);

        var back = new Button { Text = "Back" };
        back.Pressed += () =>
        {
            Settings.Save();
            OnBack?.Invoke();
        };
        AddChild(back);
    }

    private void AddSlider(string label, double min, double max, double step, double value,
                           System.Action<double> set, System.Func<double, string> show)
    {
        var name = new Label();
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value };
        name.Text = $"{label}: {show(value)}";
        slider.ValueChanged += v =>
        {
            set(v);
            name.Text = $"{label}: {show(v)}";
        };
        AddChild(name);
        AddChild(slider);
    }

    private void AddToggle(string label, bool value, System.Action<bool> set)
    {
        var box = new CheckBox { Text = label, ButtonPressed = value };
        box.Toggled += on => set(on);
        AddChild(box);
    }
}
