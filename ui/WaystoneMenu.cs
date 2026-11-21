using Godot;
using System.Collections.Generic;

// The two screens a waystone opens on the player's own machine, built in code:
//  - Naming: the first time you use a waystone, you give it a name. Only you see it.
//  - Travel: every other waystone on your list, by your name for it, with its distance.
// Esc closes either; E also closes the travel list (not the naming box, where you might be typing an e).
public partial class WaystoneMenu : CanvasLayer
{
    private Waystone _source;
    private bool _naming;

    public static void OpenNaming(Waystone source) => Open(source, naming: true);
    public static void OpenTravel(Waystone source) => Open(source, naming: false);

    private static void Open(Waystone source, bool naming)
    {
        if (GameState.MenuOpen) return;
        var menu = new WaystoneMenu { _source = source, _naming = naming, Layer = 10 };
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

        var list = new VBoxContainer { CustomMinimumSize = new Vector2(280f, 0f) };
        panel.AddChild(list);

        if (_naming) BuildNaming(list);
        else BuildTravel(list);

        var cancel = new Button { Text = "Cancel" };
        cancel.Pressed += Close;
        list.AddChild(cancel);
    }

    private void BuildNaming(VBoxContainer list)
    {
        list.AddChild(new Label { Text = "Name this waystone", HorizontalAlignment = HorizontalAlignment.Center });

        var field = new LineEdit { PlaceholderText = "Waystone", MaxLength = Waystone.MaxNameLength };
        list.AddChild(field);
        field.CallDeferred(Control.MethodName.GrabFocus);

        var save = new Button { Text = "Save" };
        list.AddChild(save);

        void Submit()
        {
            _source.RequestName(field.Text);
            Close();
        }
        save.Pressed += Submit;
        field.TextSubmitted += text => Submit();   // Enter
    }

    private void BuildTravel(VBoxContainer list)
    {
        string here = Session.LocalWaystones.GetValueOrDefault(_source.Id, "Waystone");
        list.AddChild(new Label { Text = $"{here}: travel to", HorizontalAlignment = HorizontalAlignment.Center });

        // Your list, by your names, nearest first. Stones that broke since the list arrived are skipped.
        var destinations = new List<(string Name, Waystone Stone, float Distance)>();
        StructureManager manager = StructureManager.Of(_source);
        foreach (var (id, name) in Session.LocalWaystones)
        {
            if (id == _source.Id || manager?.FindById(id) is not Waystone stone) continue;
            destinations.Add((name, stone, _source.GlobalPosition.DistanceTo(stone.GlobalPosition)));
        }
        destinations.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var (name, stone, distance) in destinations)
        {
            var button = new Button { Text = $"{name}    {distance:0} m" };
            string id = stone.Id;
            button.Pressed += () =>
            {
                _source.RequestTravel(id);
                Close();
            };
            list.AddChild(button);
        }

        if (destinations.Count == 0) list.AddChild(new Label { Text = "No other waystones on your list yet" });
    }

    // Before the pause menu sees the Esc
    public override void _Input(InputEvent e)
    {
        bool close = e.IsActionPressed("ui_cancel") || (!_naming && e.IsActionPressed("interact"));
        if (!close) return;
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
