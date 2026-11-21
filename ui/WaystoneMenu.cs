using Godot;

// The list you get when you use one of your waystones: every other waystone you own, with its distance.
// Built in code; opened by Waystone.Interact on the player's own machine. Esc or E closes it.
public partial class WaystoneMenu : CanvasLayer
{
    private Waystone _source;

    public static void Open(Waystone source)
    {
        if (GameState.MenuOpen) return;
        var menu = new WaystoneMenu { _source = source, Layer = 10 };
        source.GetTree().Root.AddChild(menu);
    }

    public override void _Ready()
    {
        GameState.MenuOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        var panel = new PanelContainer();
        AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;

        var list = new VBoxContainer();
        panel.AddChild(list);
        list.AddChild(new Label { Text = "Travel to", HorizontalAlignment = HorizontalAlignment.Center });

        // Numbered oldest first, the same order on every machine
        var mine = StructureManager.Of(this)?.OwnedBy(_source.ScenePath, _source.OwnerKey);
        int number = 0, shown = 0;
        if (mine != null)
            foreach (Structure s in mine)
            {
                number++;
                if (s is not Waystone stone || stone == _source) continue;

                float distance = _source.GlobalPosition.DistanceTo(stone.GlobalPosition);
                var button = new Button { Text = $"Waystone {number}    {distance:0} m" };
                string target = stone.Name;
                button.Pressed += () =>
                {
                    _source.RequestTravel(target);
                    Close();
                };
                list.AddChild(button);
                shown++;
            }

        if (shown == 0) list.AddChild(new Label { Text = "No other waystones yet" });

        var cancel = new Button { Text = "Cancel" };
        cancel.Pressed += Close;
        list.AddChild(cancel);
    }

    // Esc or E closes it, before the pause menu sees the Esc
    public override void _Input(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel") && !e.IsActionPressed("interact")) return;
        GetViewport().SetInputAsHandled();
        Close();
    }

    private void Close()
    {
        if (IsQueuedForDeletion()) return;
        GameState.MenuOpen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        QueueFree();
    }
}
