using Godot;

// The inventory window (I, or Inventory in the Tab menu), laid out like the sketch:
//   top left:  player view, with the armour slots beside it; focus and offhand below
//   right:     stats, then the equipped spell's stats under a divider
//   bottom:    the 9 x 3 grid
// The focus slot decides your spells. Armour and offhand slots take gear once it exists (6.8, 6.11).
// Moving items: drag and drop (ItemSlot). Shift-drag takes half, Ctrl-drag takes one, dropping outside the
// window throws it on the ground, dropping on the hotbar binds it. Sort merges stacks and orders by kind and name.
public partial class InventoryWindow : CanvasLayer
{
    private const float SlotSize = 56f;

    private PanelContainer _window;
    private readonly ItemSlot[] _slots = new ItemSlot[Inventory.Size];
    private readonly ItemSlot[] _gear = new ItemSlot[Inventory.EquipSize];
    private DropCatcher _catcher;
    private Label _stats, _spellStats;
    private Inventory _inventory;
    private float _statsTimer;

    public override void _Ready()
    {
        Layer = 10;
        AddToGroup("inventory_window");
        Build();
        _window.Visible = false;
        _catcher.Visible = false;
    }

    // ---------- Opening ----------

    public override void _Input(InputEvent e)
    {
        if (!GameState.InGame) return;

        if (e.IsActionPressed("inventory"))
        {
            if (_window.Visible) Close();
            else if (!GameState.MenuOpen) Open();
            else return;
            GetViewport().SetInputAsHandled();
        }
        else if (_window.Visible && e.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Open()
    {
        if (_window.Visible) return;
        Bind();
        _window.Visible = true;
        _catcher.Visible = true;
        GameState.MenuOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
        RefreshStats();
    }

    private void Close()
    {
        _window.Visible = false;
        _catcher.Visible = false;
        GameState.MenuOpen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // Finds this machine's player's inventory (the player can be respawned or rejoin, so look each time)
    private void Bind()
    {
        Inventory found = Inventory.Of(Me());
        if (found == _inventory) return;
        if (_inventory != null && IsInstanceValid(_inventory)) _inventory.Changed -= Refresh;
        _inventory = found;
        if (_inventory != null) _inventory.Changed += Refresh;
    }

    public override void _ExitTree()
    {
        if (_inventory != null && IsInstanceValid(_inventory)) _inventory.Changed -= Refresh;
    }

    public override void _Process(double delta)
    {
        if (!_window.Visible) return;
        _statsTimer -= (float)delta;
        if (_statsTimer > 0f) return;
        _statsTimer = 0.25f;
        RefreshStats();
    }

    // ---------- Layout ----------

    private void Build()
    {
        // Behind the window, covering the screen: drops that miss the window land here
        _catcher = new DropCatcher();
        AddChild(_catcher);
        _catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _window = new PanelContainer();
        AddChild(_window);
        _window.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _window.GrowHorizontal = Control.GrowDirection.Both;
        _window.GrowVertical = Control.GrowDirection.Both;

        var main = new VBoxContainer();
        main.AddThemeConstantOverride("separation", 10);
        _window.AddChild(main);

        // Title bar with the close button
        var bar = new HBoxContainer();
        bar.AddChild(new Label { Text = "Inventory", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var hint = new Label { Text = "Drag to move · Shift: half · Ctrl: one · drag out to drop" };
        hint.AddThemeFontSizeOverride("font_size", 11);
        bar.AddChild(hint);
        var sort = new Button { Text = "Sort" };
        sort.Pressed += () => _inventory?.RequestSort();
        bar.AddChild(sort);
        var close = new Button { Text = " X " };
        close.Pressed += Close;
        bar.AddChild(close);
        main.AddChild(bar);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        main.AddChild(top);

        // Left: player view and armour, then focus and offhand
        var left = new VBoxContainer();
        top.AddChild(left);
        var playerRow = new HBoxContainer();
        left.AddChild(playerRow);

        var view = new PanelContainer { CustomMinimumSize = new Vector2(180f, 220f) };
        view.AddChild(new Label { Text = "Player", HorizontalAlignment = HorizontalAlignment.Center });
        playerRow.AddChild(view);

        var armour = new VBoxContainer();
        armour.AddChild(Gear(EquipSlot.Head));
        armour.AddChild(Gear(EquipSlot.Chest));
        armour.AddChild(Gear(EquipSlot.Hands));
        playerRow.AddChild(armour);

        var hands = new HBoxContainer();
        hands.AddChild(Gear(EquipSlot.Focus));
        hands.AddChild(Gear(EquipSlot.Offhand));
        left.AddChild(hands);

        // Right: stats, a divider, then the equipped spell
        var stats = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300f, 0f) };
        var statsList = new VBoxContainer();
        stats.AddChild(statsList);
        _stats = new Label();
        _spellStats = new Label();
        statsList.AddChild(new Label { Text = "Stats" });
        statsList.AddChild(_stats);
        statsList.AddChild(new HSeparator());
        statsList.AddChild(new Label { Text = "Spell stats" });
        statsList.AddChild(_spellStats);
        top.AddChild(stats);

        // Bottom: the grid
        var grid = new GridContainer { Columns = Inventory.Columns };
        main.AddChild(grid);
        for (int i = 0; i < Inventory.Size; i++)
        {
            var slot = new ItemSlot { Kind = ItemSlot.SlotKind.Inventory, Index = i, CustomMinimumSize = new Vector2(SlotSize, SlotSize) };
            grid.AddChild(slot);
            _slots[i] = slot;
        }
    }

    private ItemSlot Gear(EquipSlot kind)
    {
        int index = Inventory.IndexOf(kind);
        var slot = new ItemSlot { Kind = ItemSlot.SlotKind.Equipment, Index = index, CustomMinimumSize = new Vector2(SlotSize + 20f, SlotSize) };
        _gear[index] = slot;
        return slot;
    }

    // ---------- Filling it in ----------

    private void Refresh()
    {
        if (_inventory == null || !IsInstanceValid(_inventory)) return;

        for (int i = 0; i < Inventory.Size; i++)
            _slots[i].ShowItem(ItemDatabase.Get(_inventory.IdAt(i)), _inventory.CountAt(i));

        for (int i = 0; i < Inventory.EquipSize; i++)
        {
            ItemDefinition def = ItemDatabase.Get(_inventory.EquippedAt(i));
            _gear[i].ShowItem(def, 1);
            if (def == null) _gear[i].Text = Inventory.SlotOf(i).ToString();   // empty: say what goes here
        }
        RefreshStats();
    }

    private void RefreshStats()
    {
        PlayerController me = Me();
        if (me == null) return;

        Health health = Health.Of(me);
        Mana mana = me.GetNodeOrNull<Mana>("Mana");
        Stamina stamina = me.GetNodeOrNull<Stamina>("Stamina");
        _stats.Text =
            $"Health   {health?.Current ?? 0:0} / {health?.Max ?? 0:0}\n" +
            $"Mana     {mana?.Current ?? 0:0} / {mana?.Max ?? 0:0}\n" +
            $"Stamina  {stamina?.Current ?? 0:0} / {stamina?.Max ?? 0:0}";

        SpellCaster caster = me.GetNodeOrNull<SpellCaster>("Spellcaster");
        ItemDefinition focus = ItemDatabase.Get(Inventory.Of(me)?.FocusId);
        string focusLine = focus == null ? "No focus equipped" : $"{focus.DisplayName}: {caster?.Spells.Count ?? 0} / {focus.SpellSlots} spells";

        SpellDefinition spell = caster?.ActiveSpell;
        if (spell == null)
        {
            _spellStats.Text = focusLine;
            return;
        }
        float range = caster.Stat(spell, nameof(SpellDefinition.Range));
        _spellStats.Text =
            focusLine + "\n\n" +
            $"{spell.SpellName}  ({spell.Element})\n" +
            $"Cost      {caster.CostOf(spell):0}\n" +
            $"Recharge  {caster.Stat(spell, nameof(SpellDefinition.RechargeTime)):0.0} s\n" +
            $"Range     {(range > 0f ? $"{range:0} m" : "unlimited")}";
    }

    private PlayerController Me()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is PlayerController player && player.IsMultiplayerAuthority()) return player;
        return null;
    }
}
