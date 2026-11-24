using Godot;

public enum ItemCategory { Material, Tool, Focus, Armor, Offhand, Consumable }

// Which equipment slot an item goes in. The order matches Inventory's equipment slots (minus None).
public enum EquipSlot { None, Focus, Offhand, Head, Chest, Hands }

// One kind of item. Each is a .tres in items/defs/; the game finds them all by folder, and everything
// else (loot tables, saves, recipes later) refers to them by Id.
[GlobalClass]
public partial class ItemDefinition : Resource
{
    [Export] public string Id = "";                      // unique and permanent: saves store this, so never rename it
    [Export] public string DisplayName = "Item";
    [Export(PropertyHint.MultilineText)] public string Description = "";
    [Export] public ItemCategory Category = ItemCategory.Material;
    [Export] public int MaxStack = 64;
    [Export] public Texture2D Icon;                      // hand-drawn later; until then the tint stands in
    [Export] public Color Tint = new(0.8f, 0.8f, 0.8f);
    [Export] public PackedScene Model;                   // held and dropped look; origin at the grip

    [ExportGroup("Equipment")]
    [Export] public EquipSlot EquipsTo = EquipSlot.None;
    [Export] public int SpellSlots = 0;   // foci: how many spells it holds (a glove's sigil slot counts as one for now)
}
