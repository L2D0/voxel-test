using Godot;

// A waystone: one stop on its owner's personal travel network. Interact (E) with one of your own to
// pick another of yours and travel there. Other players' waystones do nothing for you.
// Travel is free for now; it will cost a resource, scaled by distance, once that's decided.
public partial class Waystone : Structure, IInteractable
{
    [Export] public float UseReach = 6f;          // the host's check: how close you must be to the stone you used
    [Export] public float ArrivalOffset = 1.5f;   // you arrive this far in front of the destination stone

    // Where a traveller arrives: in front of the stone, which faces whoever raised it
    public Vector3 ArrivalPoint => GlobalPosition - GlobalBasis.Z * ArrivalOffset;

    // The interacting player's machine
    public void Interact(PlayerController player)
    {
        if (OwnerKey != Session.LocalKey)
        {
            GD.Print("This waystone isn't yours");
            return;
        }
        WaystoneMenu.Open(this);
    }

    // The menu picked a destination: ask the host
    public void RequestTravel(string destination)
    {
        if (Multiplayer.IsServer()) Travel(1, destination);
        else RpcId(1, MethodName.ReceiveTravel, destination);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTravel(string destination)
    {
        if (Multiplayer.IsServer()) Travel(Multiplayer.GetRemoteSenderId(), destination);
    }

    // Host: check it's really yours, you're really here, and the destination is yours too
    private void Travel(long peer, string destination)
    {
        PlayerController player = null;
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node.Name == peer.ToString()) player = node as PlayerController;
        if (player == null || player.IsDead) return;

        string key = Session.KeyOf(player);
        if (key == "" || key != OwnerKey) return;
        if (player.GlobalPosition.DistanceTo(GlobalPosition) > UseReach) return;

        if (GetParent().GetNodeOrNull<Waystone>(destination) is not Waystone target) return;
        if (target == this || target.OwnerKey != key || target.IsQueuedForDeletion()) return;

        player.TravelTo(target.ArrivalPoint);
    }
}
