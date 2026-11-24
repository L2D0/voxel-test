using Godot;

// One slot you can drag things in and out of: an inventory, equipment or hotbar slot.
//   Inventory slot: drag to move a stack. Shift-drag takes half, Ctrl-drag takes one.
//                   Drop onto another slot to move or merge, onto the hotbar to bind it, outside the window to throw it.
//   Hotbar slot:    holds a bind, not an item. Drag between hotbar slots to swap, drag off to clear,
//                   right-click to clear.
//   Equipment slot: takes only items that equip there. Drag back into the bag to unequip, out of the window to drop.
public partial class ItemSlot : Button
{
    public enum SlotKind { Inventory, Hotbar, Equipment }

    public SlotKind Kind;
    public int Index;

    private const int FromInventory = 0, FromHotbar = 1, FromEquipment = 2;

    public override void _Ready()
    {
        ClipText = true;
        ExpandIcon = true;
        FocusMode = FocusModeEnum.None;   // no keyboard focus ring; Space and Enter stay with the game
    }

    // ---------- Showing what's in it ----------

    // Placeholder look until icons are drawn: the item's tint and a short name
    public void ShowItem(ItemDefinition def, int count, bool dim = false)
    {
        Icon = def?.Icon;
        Text = def == null ? "" : def.Icon != null ? (count > 1 ? count.ToString() : "") : Short(def.DisplayName, count);
        TooltipText = def == null ? "" : $"{def.DisplayName}\n{def.Description}";
        SelfModulate = def == null ? Colors.White : def.Tint;
        Modulate = dim ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;
    }

    public void ShowSpell(SpellDefinition spell, bool dim)
    {
        Icon = null;
        Text = spell == null ? "" : Short(spell.SpellName, 1);
        TooltipText = spell?.SpellName ?? "";
        SelfModulate = new Color(0.55f, 0.65f, 1f);
        Modulate = dim ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;
    }

    public static string Short(string name, int count)
    {
        string word = (name ?? "").Split(' ')[0];
        if (word.Length > 6) word = word[..6];
        return count > 1 ? $"{word}\n{count}" : word;
    }

    // ---------- Dragging out ----------

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (LocalInventory(this) is not Inventory inventory) return default;

        if (Kind == SlotKind.Inventory)
        {
            if (inventory.IsEmpty(Index)) return default;
            int count = inventory.CountAt(Index);
            int amount = Input.IsKeyPressed(Key.Shift) ? Mathf.CeilToInt(count / 2f)
                       : Input.IsKeyPressed(Key.Ctrl) ? 1
                       : count;
            ItemDefinition def = ItemDatabase.Get(inventory.IdAt(Index));
            SetDragPreview(Preview(Short(def?.DisplayName, amount), def?.Tint ?? Colors.White));
            return new Godot.Collections.Dictionary { { "from", FromInventory }, { "slot", Index }, { "amount", amount } };
        }

        if (Kind == SlotKind.Equipment)
        {
            if (inventory.EquippedAt(Index) is not string id) return default;
            ItemDefinition def = ItemDatabase.Get(id);
            SetDragPreview(Preview(Short(def?.DisplayName, 1), def?.Tint ?? Colors.White));
            return new Godot.Collections.Dictionary { { "from", FromEquipment }, { "slot", Index }, { "amount", 1 } };
        }

        if (inventory.BindAt(Index) == "") return default;
        SetDragPreview(Preview(Text, SelfModulate));
        return new Godot.Collections.Dictionary { { "from", FromHotbar }, { "slot", Index } };
    }

    private static Control Preview(string text, Color tint)
    {
        var box = new Button { Text = text, SelfModulate = tint, CustomMinimumSize = new Vector2(52f, 52f), Modulate = new Color(1f, 1f, 1f, 0.8f) };
        box.MouseFilter = MouseFilterEnum.Ignore;
        return box;
    }

    // ---------- Dropping in ----------

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!TryRead(data, out int from, out int slot, out _)) return false;
        return Kind switch
        {
            SlotKind.Inventory => from != FromHotbar,                            // binds can't go into the bag
            SlotKind.Hotbar => from != FromEquipment,
            _ => from == FromInventory && Fits(LocalInventory(this)?.IdAt(slot)),  // only gear that goes here
        };
    }

    private bool Fits(string id) =>
        ItemDatabase.Get(id) is ItemDefinition def && Inventory.IndexOf(def.EquipsTo) == Index;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryRead(data, out int from, out int slot, out int amount)) return;
        if (LocalInventory(this) is not Inventory inventory) return;

        if (Kind == SlotKind.Equipment)
        {
            inventory.RequestEquip(slot, Index);
        }
        else if (Kind == SlotKind.Inventory)
        {
            if (from == FromEquipment) inventory.RequestUnequip(slot, Index);
            else inventory.RequestMove(slot, Index, amount);
        }
        else if (from == FromInventory)
        {
            string id = inventory.IdAt(slot);
            if (id != null) inventory.SetBind(Index, $"item:{id}");   // the item stays in the bag
        }
        else
        {
            inventory.SwapBinds(slot, Index);
        }
    }

    // Right-click a hotbar slot to clear it
    public override void _GuiInput(InputEvent e)
    {
        if (Kind != SlotKind.Hotbar || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        LocalInventory(this)?.SetBind(Index, "");
        AcceptEvent();
    }

    // ---------- Shared ----------

    public static bool TryRead(Variant data, out int from, out int slot, out int amount)
    {
        from = slot = amount = 0;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var dict = data.AsGodotDictionary();
        if (!dict.ContainsKey("from") || !dict.ContainsKey("slot")) return false;
        from = dict["from"].AsInt32();
        slot = dict["slot"].AsInt32();
        amount = dict.ContainsKey("amount") ? dict["amount"].AsInt32() : 0;
        return true;
    }

    public static bool IsFromInventory(int from) => from == FromInventory;
    public static bool IsFromEquipment(int from) => from == FromEquipment;

    // This machine's own player's inventory
    public static Inventory LocalInventory(Node anyNode)
    {
        foreach (Node node in anyNode.GetTree().GetNodesInGroup("players"))
            if (node is PlayerController player && player.IsMultiplayerAuthority()) return Inventory.Of(player);
        return null;
    }
}
