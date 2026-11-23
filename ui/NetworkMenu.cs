using Godot;
using System;

// The main menu: your worlds (play, new, delete), join a friend, settings, quit.
// Built in code on top of the old Host/Join scene, whose controls it clears away.
// It sits over the world until a session starts, then hides.
public partial class NetworkMenu : CanvasLayer
{
    [Export] public VoxelWorld World;

    private NetworkManager _net;
    private CenterContainer _centre;

    public override void _Ready()
    {
        _net = GetNode<NetworkManager>("/root/NetworkManager");
        Settings.Load();
        Settings.ApplyShadows(GetTree().Root);
        Input.MouseMode = Input.MouseModeEnum.Visible;

        foreach (Node child in GetChildren()) child.QueueFree();   // the old Host/Join controls

        var background = new ColorRect { Color = new Color(0.08f, 0.08f, 0.1f, 0.9f) };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _centre = new CenterContainer();
        AddChild(_centre);
        _centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        ShowTitle();
    }

    // ---------- Pages ----------

    private void ShowTitle()
    {
        VBoxContainer page = NewPage(null);
        page.AddChild(new Label
        {
            Text = "Spellwright",
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = 48 },
        });
        AddButton(page, "Play", ShowWorlds);
        AddButton(page, "Join a world", ShowJoin);
        AddButton(page, "Settings", ShowSettings);
        AddButton(page, "Quit", () => GetTree().Quit());
    }

    private void ShowWorlds()
    {
        VBoxContainer page = NewPage("Your worlds");

        var worlds = WorldSave.ListWorlds();
        foreach (var (name, _) in worlds)
        {
            var row = new HBoxContainer();
            var play = new Button { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            play.Pressed += () => PlayWorld(name);
            var delete = new Button { Text = "Delete" };
            delete.Pressed += () => ShowDelete(name);
            row.AddChild(play);
            row.AddChild(delete);
            page.AddChild(row);
        }
        if (worlds.Count == 0) page.AddChild(new Label { Text = "No worlds yet", HorizontalAlignment = HorizontalAlignment.Center });

        AddButton(page, "New world", ShowNewWorld);
        AddButton(page, "Back", ShowTitle);
    }

    private void ShowDelete(string name)
    {
        VBoxContainer page = NewPage($"Delete '{name}'?");
        page.AddChild(new Label { Text = "This can't be undone.", HorizontalAlignment = HorizontalAlignment.Center });
        AddButton(page, "Delete", () =>
        {
            WorldSave.Delete(name);
            ShowWorlds();
        });
        AddButton(page, "Cancel", ShowWorlds);
    }

    private void ShowNewWorld()
    {
        VBoxContainer page = NewPage("New world");

        page.AddChild(new Label { Text = "Name" });
        var name = new LineEdit { Text = "New World", MaxLength = 32 };
        page.AddChild(name);

        page.AddChild(new Label { Text = "Seed" });
        var seed = new LineEdit { PlaceholderText = "Random" };
        page.AddChild(seed);

        var friendlyFire = new CheckBox { Text = "Friendly fire" };
        page.AddChild(friendlyFire);

        AddButton(page, "Create", () =>
        {
            // Never overwrite: a taken name gets a number on the end
            string clean = WorldSave.CleanName(name.Text);
            string unique = clean;
            for (int n = 2; WorldSave.Exists(unique); n++) unique = $"{clean} {n}";

            WorldSave.Current = WorldSave.CreateNew(unique, ParseSeed(seed.Text), friendlyFire.ButtonPressed);
            WorldSave.Current.Save();   // so it shows in the list even if the game closes before the first autosave
            StartHost();
        });
        AddButton(page, "Back", ShowWorlds);
    }

    private void ShowJoin()
    {
        VBoxContainer page = NewPage("Join a world");
        page.AddChild(new Label { Text = "Host address" });
        var address = new LineEdit { Text = "127.0.0.1" };
        page.AddChild(address);

        AddButton(page, "Join", () =>
        {
            WorldSave.Current = null;   // clients don't save; the host does
            _net.Join(address.Text.StripEdges());
            Visible = false;
        });
        AddButton(page, "Back", ShowTitle);
    }

    private void ShowSettings()
    {
        ClearPage();
        _centre.AddChild(new SettingsPanel { OnBack = ShowTitle });
    }

    // ---------- Starting ----------

    private void PlayWorld(string name)
    {
        WorldSave.Current = WorldSave.Load(name);
        if (WorldSave.Current == null)
        {
            VBoxContainer page = NewPage($"Couldn't open '{name}'");
            page.AddChild(new Label { Text = "Check the Output panel for why." });
            AddButton(page, "Back", ShowWorlds);
            return;
        }
        StartHost();
    }

    private void StartHost()
    {
        _net.Host(WorldSave.Current.Seed);
        Visible = false;
    }

    // Blank is random. A number is used as it is; anything else is turned into a number, so "rabbits" is a seed too.
    private static int ParseSeed(string text)
    {
        text = text.StripEdges();
        if (text == "") return (int)GD.Randi();
        return int.TryParse(text, out int number) ? number : (int)GD.Hash(text);
    }

    // ---------- Helpers ----------

    private void ClearPage()
    {
        foreach (Node child in _centre.GetChildren()) child.QueueFree();
    }

    private VBoxContainer NewPage(string title)
    {
        ClearPage();
        var page = new VBoxContainer { CustomMinimumSize = new Vector2(320f, 0f) };
        _centre.AddChild(page);
        if (title != null) page.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center });
        return page;
    }

    private static void AddButton(VBoxContainer page, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        page.AddChild(button);
    }
}
