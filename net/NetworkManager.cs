using Godot;

// Hosting, joining and leaving. An autoload, so it outlives the scene: anything it subscribes to has to be
// unsubscribed in Leave, or the next session would fire it twice.
public partial class NetworkManager : Node
{
    public const int Port = 7777;
    public const int MaxPlayers = 8;

    [Signal] public delegate void SessionStartedEventHandler(int seed);
    [Signal] public delegate void SessionEndedEventHandler();   // client: the host went away, or never answered

    public int WorldSeed { get; private set; }

    private bool _hosting, _joined;

    public void Host(int seed)
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(Port, MaxPlayers);
        if (err != Error.Ok)
        {
            GD.PushError($"Couldn't host on port {Port}: {err}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += SendSeed;
        _hosting = true;

        WorldSeed = seed;
        EmitSignal(SignalName.SessionStarted, seed);
    }

    public void Join(string address)
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, Port);
        if (err != Error.Ok)
        {
            GD.PushError($"Couldn't connect to {address}: {err}");
            EmitSignal(SignalName.SessionEnded);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ServerDisconnected += OnServerLost;
        Multiplayer.ConnectionFailed += OnServerLost;
        _joined = true;
        // The session starts once the host sends us the world seed
    }

    // Back to no connection at all
    public void Leave()
    {
        if (_hosting) Multiplayer.PeerConnected -= SendSeed;
        if (_joined)
        {
            Multiplayer.ServerDisconnected -= OnServerLost;
            Multiplayer.ConnectionFailed -= OnServerLost;
        }
        _hosting = _joined = false;

        Multiplayer.MultiplayerPeer?.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        WorldSeed = 0;
    }

    private void SendSeed(long id) => RpcId(id, MethodName.ReceiveSeed, WorldSeed);

    private void OnServerLost() => EmitSignal(SignalName.SessionEnded);

    // Only the host (multiplayer authority, peer 1) may call this
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSeed(int seed)
    {
        WorldSeed = seed;
        EmitSignal(SignalName.SessionStarted, seed);
    }
}
