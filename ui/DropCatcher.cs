using Godot;

// Covers the screen behind the inventory window. Dropping an item here throws it on the ground in front
// of you (equipped gear too); dropping a hotbar bind here clears it.
public partial class DropCatcher : Control
{
    public override bool _CanDropData(Vector2 atPosition, Variant data) => ItemSlot.TryRead(data, out _, out _, out _);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!ItemSlot.TryRead(data, out int from, out int slot, out int amount)) return;
        if (ItemSlot.LocalInventory(this) is not Inventory inventory) return;

        if (ItemSlot.IsFromInventory(from)) inventory.RequestDrop(slot, amount);
        else if (ItemSlot.IsFromEquipment(from)) inventory.RequestDropEquipped(slot);
        else inventory.SetBind(slot, "");
    }
}
