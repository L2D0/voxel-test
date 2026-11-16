using Godot;

public partial class NetworkMenu : CanvasLayer
{
    [Export] public VoxelWorld World;

    public override void _Ready()
    {
        var net = GetNode<NetworkManager>("/root/NetworkManager");
        var address = GetNode<LineEdit>("%AddressEdit");

        Input.MouseMode = Input.MouseModeEnum.Visible;

        GetNode<Button>("%HostButton").Pressed += () =>
        {
            net.Host(World.Seed);
            Visible = false;
        };

        GetNode<Button>("%JoinButton").Pressed += () =>
        {
            net.Join(address.Text);
            Visible = false;
        };
    }
}