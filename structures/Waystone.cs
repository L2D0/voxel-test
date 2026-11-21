using Godot;

// A waystone: anyone can use any waystone, and each player keeps their own list of the ones they've used.
// The first time you use one, you name it (just for you) and it goes on your list. After that, using it
// shows your list so you can travel to any other waystone on it. Breaking a waystone takes it off every list.
// Travel is free for now; it will cost a resource, scaled by distance, once that's decided.
public partial class Waystone : Structure, IInteractable
{
    [Export] public float UseReach = 6f;          // the host's check: how close you must be to the stone you used
    [Export] public float ArrivalOffset = 1.5f;   // you arrive this far in front of the destination stone
    public const int MaxNameLength = 24;

    // Where a traveller arrives: in front of the stone, which faces whoever raised it
    public Vector3 ArrivalPoint => GlobalPosition - GlobalBasis.Z * ArrivalOffset;

    // ---------- The interacting player's machine ----------

    public void Interact(PlayerController player)
    {
        if (Session.LocalWaystones.ContainsKey(Id)) WaystoneMenu.OpenTravel(this);
        else WaystoneMenu.OpenNaming(this);
    }

    public void RequestName(string name)
    {
        if (Multiplayer.IsServer()) NameFor(1, name);
        else RpcId(1, MethodName.ReceiveName, name);
    }

    public void RequestTravel(string destinationId)
    {
        if (Multiplayer.IsServer()) Travel(1, destinationId);
        else RpcId(1, MethodName.ReceiveTravel, destinationId);
    }

    // ---------- Host ----------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveName(string name)
    {
        if (Multiplayer.IsServer()) NameFor(Multiplayer.GetRemoteSenderId(), name);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTravel(string destinationId)
    {
        if (Multiplayer.IsServer()) Travel(Multiplayer.GetRemoteSenderId(), destinationId);
    }

    private void NameFor(long peer, string name)
    {
        if (UserAt(peer) is not PlayerController player) return;
        Session.Learn(player, Id, Clean(name));
    }

    // Both ends have to be on your list, and you have to be standing at this one
    private void Travel(long peer, string destinationId)
    {
        if (UserAt(peer) is not PlayerController player) return;
        if (!Session.Knows(player, Id) || !Session.Knows(player, destinationId)) return;

        if (StructureManager.Of(this)?.FindById(destinationId) is not Waystone target || target == this) return;
        player.TravelTo(target.ArrivalPoint);
    }

    // The player for this peer, if they're alive and close enough to be using this stone
    private PlayerController UserAt(long peer)
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node.Name == peer.ToString() && node is PlayerController player && !player.IsDead
                && player.GlobalPosition.DistanceTo(GlobalPosition) <= UseReach)
                return player;
        return null;
    }

    private static string Clean(string name)
    {
        name = (name ?? "").StripEdges();
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        return name == "" ? "Waystone" : name;
    }
}
