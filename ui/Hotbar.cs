using Godot;

// The 10-slot hotbar along the bottom of the screen. It holds binds, not items: an item bind shows how many
// of that item you carry (greyed out at none), a spell bind selects that spell when chosen (greyed out if the
// spell isn't in your current loadout). Keys 1 to 0 choose a slot; the scroll wheel scrubs along it.
// Bind items by dragging them from the inventory; bind spells by hovering one in the spell wheel and
// pressing a number key.
public partial class Hotbar : CanvasLayer
{
    private const float SlotSize = 56f;

    private readonly ItemSlot[] _slots = new ItemSlot[Inventory.HotbarSize];
    private readonly Label[] _keys = new Label[Inventory.HotbarSize];
    private Inventory _inventory;
    private int _selected;
    private float _refreshTimer;

    public static Hotbar Of(Node anyNode) => anyNode.GetTree().GetFirstNodeInGroup("hotbar") as Hotbar;

    // Number row: 1 to 9 are slots 0 to 8, 0 is slot 9
    public static int? SlotForKey(Key key) => key switch
    {
        >= Key.Key1 and <= Key.Key9 => (int)(key - Key.Key1),
        Key.Key0 => 9,
        _ => null,
    };

    public override void _Ready()
    {
        Layer = 11;   // above the inventory window, so drags can land on it
        AddToGroup("hotbar");

        var row = new HBoxContainer();
        AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        row.GrowHorizontal = Control.GrowDirection.Both;
        row.GrowVertical = Control.GrowDirection.Begin;
        row.Position += new Vector2(0f, -8f);

        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            var slot = new ItemSlot { Kind = ItemSlot.SlotKind.Hotbar, Index = i, CustomMinimumSize = new Vector2(SlotSize, SlotSize) };
            var key = new Label { Text = i == 9 ? "0" : (i + 1).ToString(), Position = new Vector2(3f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            key.AddThemeFontSizeOverride("font_size", 11);
            slot.AddChild(key);
            row.AddChild(slot);
            _slots[i] = slot;
            _keys[i] = key;
        }
        Visible = false;   // shown once this machine's player exists
    }

    public override void _ExitTree()
    {
        if (_inventory != null && IsInstanceValid(_inventory)) _inventory.Changed -= Refresh;
    }

    // ---------- Choosing a slot ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!GameState.InGame || GameState.MenuOpen) return;

        if (e is InputEventKey { Pressed: true, Echo: false } key && SlotForKey(key.PhysicalKeycode) is int slot)
        {
            Select(slot);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true } wheel
                 && (wheel.ButtonIndex == MouseButton.WheelDown || wheel.ButtonIndex == MouseButton.WheelUp))
        {
            int step = wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
            Select((int)Mathf.PosMod(_selected + step, Inventory.HotbarSize));
            GetViewport().SetInputAsHandled();
        }
    }

    private void Select(int slot)
    {
        _selected = slot;
        if (_inventory != null && SpellOf(_inventory.BindAt(slot)) is int index) Caster()?.SelectSpell(index);
        Refresh();
    }

    // ---------- Binding ----------

    public void BindSpell(int slot, SpellDefinition spell)
    {
        Bind();
        if (spell != null && !string.IsNullOrEmpty(spell.ResourcePath)) _inventory?.SetBind(slot, $"spell:{spell.ResourcePath}");
    }

    // ---------- Showing it ----------

    public override void _Process(double delta)
    {
        Visible = GameState.InGame;
        if (!Visible) return;

        // Slots only take the mouse while a menu is open; otherwise clicks belong to the game
        Control.MouseFilterEnum filter = GameState.MenuOpen ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        foreach (ItemSlot slot in _slots) slot.MouseFilter = filter;

        _refreshTimer -= (float)delta;
        if (_refreshTimer > 0f) return;
        _refreshTimer = 0.25f;   // also catches the player respawning or the loadout changing
        Bind();
        Refresh();
    }

    private void Bind()
    {
        Inventory found = ItemSlot.LocalInventory(this);
        if (found == _inventory) return;
        if (_inventory != null && IsInstanceValid(_inventory)) _inventory.Changed -= Refresh;
        _inventory = found;
        if (_inventory != null) _inventory.Changed += Refresh;
    }

    private void Refresh()
    {
        if (_inventory == null || !IsInstanceValid(_inventory)) return;
        SpellCaster caster = Caster();

        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            ItemSlot slot = _slots[i];
            string bind = _inventory.BindAt(i);

            if (bind.StartsWith("item:"))
            {
                string id = bind[5..];
                int count = _inventory.CountOf(id);
                slot.ShowItem(ItemDatabase.Get(id), count, dim: count == 0);
            }
            else if (bind.StartsWith("spell:"))
            {
                int? index = SpellOf(bind);
                SpellDefinition spell = index is int n ? caster.Spells[n]
                    : ResourceLoader.Exists(bind[6..]) ? GD.Load<SpellDefinition>(bind[6..]) : null;
                slot.ShowSpell(spell, dim: index == null);
            }
            else slot.ShowItem(null, 0);

            // The chosen slot stands out
            if (i == _selected) slot.Modulate = slot.Modulate with { R = 1f, G = 1f, B = 0.55f };
        }
    }

    // Where a spell bind sits in the current loadout, if it's there at all
    private int? SpellOf(string bind)
    {
        if (!bind.StartsWith("spell:") || Caster() is not SpellCaster caster) return null;
        string path = bind[6..];
        for (int i = 0; i < caster.Spells.Count; i++)
            if (caster.Spells[i]?.ResourcePath == path) return i;
        return null;
    }

    private SpellCaster Caster() => _inventory?.GetParent()?.GetNodeOrNull<SpellCaster>("Spellcaster");
}
