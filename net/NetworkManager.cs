using Godot;

public partial class NetworkManager : Node
{
    public const int Port = 7777;
    public const int MaxPlayers = 8;

    [Signal] public delegate void SessionStartedEventHandler(int seed);

    public int WorldSeed { get; private set; }

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
        Multiplayer.PeerConnected += id => RpcId(id, MethodName.ReceiveSeed, WorldSeed);

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
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        // The session starts once the host sends us the world seed
    }

    // Only the host (multiplayer authority, peer 1) may call this
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSeed(int seed)
    {
        WorldSeed = seed;
        EmitSignal(SignalName.SessionStarted, seed);
    }
}