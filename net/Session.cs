using Godot;

public partial class Session : Node
{
    [Export] public PackedScene PlayerScene;
    [Export] public VoxelWorld World;
    [Export] public Node3D Players;
    [Export] public MultiplayerSpawner Spawner;
    [Export] public Vector3 SpawnPoint = new Vector3(0, 70, 0);
    [Export] public bool FriendlyFire = false;   // world rule; chosen when creating a world once the main menu exists (4.7)
    [Export] public Key FriendlyFireDebugKey = Key.F8;   // host only: flips it mid-game for testing

    public override void _Ready()
    {
        if (PlayerScene == null || World == null || Players == null || Spawner == null)
        {
            GD.PushError("Session is missing PlayerScene, World, Players or Spawner");
            return;
        }

        // Runs on every machine whenever the host calls Spawner.Spawn(...)
        Spawner.SpawnFunction = Callable.From<Variant, Node>(CreatePlayer);

        GetNode<NetworkManager>("/root/NetworkManager").SessionStarted += OnSessionStarted;
    }

    private void OnSessionStarted(int seed)
    {
        World.Initialize(seed);

        if (!Multiplayer.IsServer()) return;   // only the host spawns players and sets the rules

        Teams.FriendlyFire = FriendlyFire;   // static: set every session, so an old value never carries over
        SpawnPlayer(1);
        Multiplayer.PeerConnected += SpawnPlayer;
        Multiplayer.PeerConnected += id => RpcId(id, MethodName.ReceiveRules, Teams.FriendlyFire);
        Multiplayer.PeerDisconnected += id => Players.GetNodeOrNull(id.ToString())?.QueueFree();
    }

    // ---------- World rules ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRules(bool friendlyFire) => Teams.FriendlyFire = friendlyFire;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != FriendlyFireDebugKey) return;
        if (!Multiplayer.IsServer() || !GameState.InGame) return;

        Teams.FriendlyFire = !Teams.FriendlyFire;
        Rpc(MethodName.ReceiveRules, Teams.FriendlyFire);
        GD.Print($"Friendly fire {(Teams.FriendlyFire ? "on" : "off")}");
    }

    // ---------- Players ----------

    // Host only: decide the spawn data and broadcast it
    private void SpawnPlayer(long id)
    {
        Vector3 position = SpawnPoint + new Vector3((id % 8) * 2f, 0, 0);
        Spawner.Spawn(new Godot.Collections.Array { id, position });
    }

    // Every machine: build the player from the broadcast data
    private Node CreatePlayer(Variant data)
    {
        var args = data.AsGodotArray();
        var player = PlayerScene.Instantiate<PlayerController>();
        player.Name = args[0].AsInt64().ToString();   // the node's name *is* the owning peer's id
        player.Position = args[1].AsVector3();
        return player;   // the spawner adds it under Players itself
    }
}