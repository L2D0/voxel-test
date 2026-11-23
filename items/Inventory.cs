using Godot;
using System;

// A player's inventory: 27 slots (9 x 3). The host's copy is the real one, so items can't be duplicated by a
// client; every change happens on the host, which then sends the owner a fresh copy. Other players' machines
// don't hold anyone else's items. Lives on the player as a child node named "Inventory".
public partial class Inventory : Node
{
    public const int Columns = 9, Rows = 3, Size = Columns * Rows;

    public event Action Changed;   // every machine that holds a copy: something moved

    private readonly string[] _ids = new string[Size];
    private readonly int[] _counts = new int[Size];

    public static Inventory Of(Node player) => player?.GetNodeOrNull<Inventory>("Inventory");

    public string IdAt(int slot) => IsEmpty(slot) ? null : _ids[slot];
    public int CountAt(int slot) => IsEmpty(slot) ? 0 : _counts[slot];
    public bool IsEmpty(int slot) => string.IsNullOrEmpty(_ids[slot]) || _counts[slot] <= 0;

    public int CountOf(string id)
    {
        int total = 0;
        for (int i = 0; i < Size; i++) if (_ids[i] == id) total += _counts[i];
        return total;
    }

    // ---------- Host: changing it ----------

    // Tops up stacks of the same item first, then fills empty slots. Returns what didn't fit.
    public int Add(string id, int count)
    {
        if (!Multiplayer.IsServer() || count <= 0 || ItemDatabase.Get(id) == null) return count;
        int max = ItemDatabase.MaxStackOf(id);

        for (int i = 0; i < Size && count > 0; i++)
        {
            if (_ids[i] != id || _counts[i] >= max) continue;
            int moved = Math.Min(count, max - _counts[i]);
            _counts[i] += moved;
            count -= moved;
        }
        for (int i = 0; i < Size && count > 0; i++)
        {
            if (!IsEmpty(i)) continue;
            int moved = Math.Min(count, max);
            _ids[i] = id;
            _counts[i] = moved;
            count -= moved;
        }
        Sync();
        return count;
    }

    // Takes this many out, from anywhere. False (and nothing taken) if there aren't enough.
    public bool Remove(string id, int count)
    {
        if (!Multiplayer.IsServer() || CountOf(id) < count) return false;
        for (int i = Size - 1; i >= 0 && count > 0; i--)
        {
            if (_ids[i] != id) continue;
            int taken = Math.Min(count, _counts[i]);
            _counts[i] -= taken;
            count -= taken;
            if (_counts[i] <= 0) Clear(i);
        }
        Sync();
        return true;
    }

    // Same item: merge as much as fits. Different item or empty: swap.
    private void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Size || to >= Size || IsEmpty(from)) return;

        if (_ids[to] == _ids[from])
        {
            int moved = Math.Min(_counts[from], ItemDatabase.MaxStackOf(_ids[to]) - _counts[to]);
            _counts[to] += moved;
            _counts[from] -= moved;
            if (_counts[from] <= 0) Clear(from);
        }
        else
        {
            (_ids[from], _ids[to]) = (_ids[to], _ids[from]);
            (_counts[from], _counts[to]) = (_counts[to], _counts[from]);
        }
        Sync();
    }

    private void Clear(int slot)
    {
        _ids[slot] = null;
        _counts[slot] = 0;
    }

    // ---------- The owner asking ----------

    public void RequestMove(int from, int to)
    {
        if (Multiplayer.IsServer()) Move(from, to);
        else RpcId(1, MethodName.ReceiveMove, from, to);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMove(int from, int to)
    {
        if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == GetMultiplayerAuthority()) Move(from, to);
    }

    // Debug panel: five of every item
    public void DebugGiveAll()
    {
        if (Multiplayer.IsServer()) GiveAll();
        else RpcId(1, MethodName.ReceiveDebugGive);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveDebugGive()
    {
        if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == GetMultiplayerAuthority()) GiveAll();
    }

    private void GiveAll()
    {
        foreach (ItemDefinition def in ItemDatabase.All) Add(def.Id, 5);
    }

    // ---------- Keeping the owner's copy current ----------

    private void Sync()
    {
        Changed?.Invoke();   // the host's own window, if it's the host's inventory
        long owner = GetMultiplayerAuthority();
        if (owner != Multiplayer.GetUniqueId()) RpcId(owner, MethodName.ReceiveSlots, SlotIds(), (int[])_counts.Clone());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSlots(string[] ids, int[] counts)
    {
        if (Multiplayer.GetRemoteSenderId() != 1) return;   // only the host's copy is real
        for (int i = 0; i < Size; i++)
        {
            _ids[i] = i < ids.Length && ids[i] != "" ? ids[i] : null;
            _counts[i] = i < counts.Length ? counts[i] : 0;
        }
        Changed?.Invoke();
    }

    // ---------- Saving (host) ----------

    public string[] SlotIds()
    {
        var ids = new string[Size];
        for (int i = 0; i < Size; i++) ids[i] = IsEmpty(i) ? "" : _ids[i];
        return ids;
    }

    public int[] SlotCounts() => (int[])_counts.Clone();

    public void Load(string[] ids, int[] counts)
    {
        if (!Multiplayer.IsServer()) return;
        for (int i = 0; i < Size; i++)
        {
            string id = i < ids.Length ? ids[i] : "";
            int count = i < counts.Length ? counts[i] : 0;
            bool known = id != "" && ItemDatabase.Get(id) != null;   // an item removed from the game since the save
            _ids[i] = known && count > 0 ? id : null;
            _counts[i] = known ? count : 0;
        }
        Sync();
    }
}
