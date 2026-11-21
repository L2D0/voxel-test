using Godot;

public partial class NetworkMenu : CanvasLayer
{
    [Export] public VoxelWorld World;
    [Export] public string WorldName = "default";   // one world slot until the main menu exists (4.7)

    public override void _Ready()
    {
        var net = GetNode<NetworkManager>("/root/NetworkManager");
        var address = GetNode<LineEdit>("%AddressEdit");
        var host = GetNode<Button>("%HostButton");

        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Host: carries on the saved world if there is one, otherwise starts a new one
        host.Text = WorldSave.Exists(WorldName) ? "Host (continue world)" : "Host (new world)";
        host.Pressed += () =>
        {
            HostWorld(net, fresh: false);
            Visible = false;
        };

        // A second button to throw the saved world away and start over
        var fresh = new Button { Text = "Host new world (deletes save)" };
        host.GetParent().AddChild(fresh);
        host.GetParent().MoveChild(fresh, host.GetIndex() + 1);
        fresh.Visible = WorldSave.Exists(WorldName);
        fresh.Pressed += () =>
        {
            HostWorld(net, fresh: true);
            Visible = false;
        };

        GetNode<Button>("%JoinButton").Pressed += () =>
        {
            net.Join(address.Text);
            Visible = false;
        };
    }

    private void HostWorld(NetworkManager net, bool fresh)
    {
        if (fresh) WorldSave.Delete(WorldName);

        WorldSave.Current = WorldSave.Exists(WorldName)
            ? WorldSave.Load(WorldName)
            : null;
        // A new world gets a random seed; the friendly fire default comes from Session once it starts
        WorldSave.Current ??= WorldSave.CreateNew(WorldName, (int)GD.Randi(), false);

        net.Host(WorldSave.Current.Seed);
    }
}
