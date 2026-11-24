using Godot;
using System;
using System.Collections.Generic;

// A player's inventory: 27 slots (9 x 3) plus 10 hotbar binds. The host's copy is the real one, so items
// can't be duplicated by a client; every change happens on the host, which then sends the owner a fresh copy.
// Lives on the player as a child node named "Inventory".
//
// The hotbar holds binds, not items: "item:<id>" points at whatever of that item you carry, "spell:<path>"
// at a spell. The owner changes binds; the host only keeps them so they're saved with the character.
public partial class Inventory : Node
{
    public const int Columns = 9, Rows = 3, Size = Columns * Rows;
    public const int HotbarSize = 10;
    private const float ThrowDistance = 1.6f;   // dropped items land this far in front of you
    private const float ThrowPickupDelay = 2f;  // long enough to walk away from what you dropped

    public event Action Changed;   // every machine that holds a copy: items or binds changed

    private readonly string[] _ids = new string[Size];
    private readonly int[] _counts = new int[Size];
    private readonly string[] _hotbar = new string[HotbarSize];

    public static Inventory Of(Node player) => player?.GetNodeOrNull<Inventory>("Inventory");

    public string IdAt(int slot) => IsEmpty(slot) ? null : _ids[slot];
    public int CountAt(int slot) => IsEmpty(slot) ? 0 : _counts[slot];
    public bool IsEmpty(int slot) => string.IsNullOrEmpty(_ids[slot]) || _counts[slot] <= 0;
    public string BindAt(int slot) => slot >= 0 && slot < HotbarSize ? _hotbar[slot] ?? "" : "";

    public int CountOf(string id)
    {
        int total = 0;
        for (int i = 0; i < Size; i++) if (_ids[i] == id) total += _counts[i];
        return total;
    }

    // ---------- Host: changing items ----------

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

    // Moves some or all of a stack. Into an empty slot: just goes there. Onto the same item: merges as much
    // as fits. Onto a different item: swaps, but only when the whole stack is moving.
    private void Move(int from, int to, int amount)
    {
        if (from == to || !Valid(from) || !Valid(to) || IsEmpty(from)) return;
        amount = Math.Clamp(amount, 1, _counts[from]);

        if (IsEmpty(to))
        {
            _ids[to] = _ids[from];
            _counts[to] = amount;
            _counts[from] -= amount;
        }
        else if (_ids[to] == _ids[from])
        {
            int moved = Math.Min(amount, ItemDatabase.MaxStackOf(_ids[to]) - _counts[to]);
            _counts[to] += moved;
            _counts[from] -= moved;
        }
        else if (amount == _counts[from])
        {
            (_ids[from], _ids[to]) = (_ids[to], _ids[from]);
            (_counts[from], _counts[to]) = (_counts[to], _counts[from]);
        }
        else return;   // half a stack onto a different item: nowhere for it to go

        if (_counts[from] <= 0) Clear(from);
        Sync();
    }

    // Out of the inventory and onto the ground in front of you
    private void Drop(int slot, int amount)
    {
        if (!Valid(slot) || IsEmpty(slot) || GetParent() is not PlayerController player) return;
        amount = Math.Clamp(amount, 1, _counts[slot]);
        string id = _ids[slot];

        _counts[slot] -= amount;
        if (_counts[slot] <= 0) Clear(slot);
        Sync();

        Vector3 forward = -player.GetNode<Node3D>("CameraPivot").GlobalBasis.Z;
        forward = new Vector3(forward.X, 0f, forward.Z).Normalized();
        ItemDropManager.Of(this)?.Drop(id, amount, player.GlobalPosition + forward * ThrowDistance, ThrowPickupDelay);
    }

    // Merges partial stacks, then orders by category and name
    private void Sort()
    {
        var totals = new Dictionary<string, int>();
        for (int i = 0; i < Size; i++)
        {
            if (IsEmpty(i)) continue;
            totals[_ids[i]] = totals.GetValueOrDefault(_ids[i]) + _counts[i];
            Clear(i);
        }

        var order = new List<string>(totals.Keys);
        order.Sort((a, b) =>
        {
            ItemDefinition da = ItemDatabase.Get(a), db = ItemDatabase.Get(b);
            int byCategory = (da?.Category ?? 0).CompareTo(db?.Category ?? 0);
            return byCategory != 0 ? byCategory : string.Compare(da?.DisplayName, db?.DisplayName, StringComparison.Ordinal);
        });

        int slot = 0;
        foreach (string id in order)
        {
            int left = totals[id], max = ItemDatabase.MaxStackOf(id);
            while (left > 0 && slot < Size)
            {
                _ids[slot] = id;
                _counts[slot] = Math.Min(left, max);
                left -= _counts[slot];
                slot++;
            }
        }
        Sync();
    }

    private void Clear(int slot)
    {
        _ids[slot] = null;
        _counts[slot] = 0;
    }

    private static bool Valid(int slot) => slot >= 0 && slot < Size;

    // ---------- The owner asking ----------

    public void RequestMove(int from, int to, int amount)
    {
        if (Multiplayer.IsServer()) Move(from, to, amount);
        else RpcId(1, MethodName.ReceiveMove, from, to, amount);
    }

    public void RequestDrop(int slot, int amount)
    {
        if (Multiplayer.IsServer()) Drop(slot, amount);
        else RpcId(1, MethodName.ReceiveDrop, slot, amount);
    }

    public void RequestSort()
    {
        if (Multiplayer.IsServer()) Sort();
        else RpcId(1, MethodName.ReceiveSort);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMove(int from, int to, int amount)
    {
        if (FromOwner()) Move(from, to, amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveDrop(int slot, int amount)
    {
        if (FromOwner()) Drop(slot, amount);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSort()
    {
        if (FromOwner()) Sort();
    }

    private bool FromOwner() => Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == GetMultiplayerAuthority();

    // Debug panel: five of every item
    public void DebugGiveAll()
    {
        if (Multiplayer.IsServer()) GiveAll();
        else RpcId(1, MethodName.ReceiveDebugGive);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveDebugGive()
    {
        if (FromOwner()) GiveAll();
    }

    private void GiveAll()
    {
        foreach (ItemDefinition def in ItemDatabase.All) Add(def.Id, 5);
    }

    // ---------- Hotbar binds (owner changes, host keeps) ----------

    public void SetBind(int slot, string bind)
    {
        if (slot < 0 || slot >= HotbarSize) return;
        _hotbar[slot] = bind ?? "";
        Changed?.Invoke();
        if (!Multiplayer.IsServer()) RpcId(1, MethodName.ReceiveBinds, HotbarBinds());
    }

    public void SwapBinds(int a, int b)
    {
        if (a < 0 || b < 0 || a >= HotbarSize || b >= HotbarSize || a == b) return;
        (_hotbar[a], _hotbar[b]) = (_hotbar[b], _hotbar[a]);
        Changed?.Invoke();
        if (!Multiplayer.IsServer()) RpcId(1, MethodName.ReceiveBinds, HotbarBinds());
    }

    public string[] HotbarBinds()
    {
        var binds = new string[HotbarSize];
        for (int i = 0; i < HotbarSize; i++) binds[i] = _hotbar[i] ?? "";
        return binds;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBinds(string[] binds)
    {
        if (!FromOwner() && Multiplayer.GetRemoteSenderId() != 1) return;   // owner to host, or host to owner on load
        for (int i = 0; i < HotbarSize; i++) _hotbar[i] = i < binds.Length ? binds[i] : "";
        Changed?.Invoke();
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

    public void Load(string[] ids, int[] counts, string[] binds)
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
        for (int i = 0; i < HotbarSize; i++) _hotbar[i] = binds != null && i < binds.Length ? binds[i] : "";
        Sync();

        long owner = GetMultiplayerAuthority();
        if (owner != Multiplayer.GetUniqueId()) RpcId(owner, MethodName.ReceiveBinds, HotbarBinds());
    }
}
