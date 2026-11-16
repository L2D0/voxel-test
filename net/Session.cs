using Godot;

public partial class Session : Node
{
    [Export] public PackedScene PlayerScene;
    [Export] public VoxelWorld World;
    [Export] public Node3D Players;
    [Export] public Vector3 SpawnPoint = new Vector3(0, 70, 0);

    public override void _Ready()
    {
        GetNode<NetworkManager>("/root/NetworkManager").SessionStarted += OnSessionStarted;
    }

    private void OnSessionStarted(int seed)
    {
        World.Initialize(seed);

        if (!Multiplayer.IsServer()) return;   // only the host spawns players

        SpawnPlayer(1);
        Multiplayer.PeerConnected += SpawnPlayer;
        Multiplayer.PeerDisconnected += id => Players.GetNodeOrNull(id.ToString())?.QueueFree();
    }

    private void SpawnPlayer(long id)
    {
        var player = PlayerScene.Instantiate<PlayerController>();
        player.Name = id.ToString();   // the node's name *is* the owning peer's id
        player.Position = SpawnPoint + new Vector3((id % 8) * 2f, 0, 0);
        Players.AddChild(player, true);
    }
}