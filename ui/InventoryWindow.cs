using Godot;

// The inventory window (I, or Inventory in the Tab menu), laid out like the sketch:
//   top left:  player view, with the armour slots beside it; focus and offhand below
//   right:     stats, then the equipped spell's stats under a divider
//   bottom:    the 9 x 3 grid
// The armour, focus and offhand slots are placeholders until gear exists (6.5, 6.8, 6.11).
// Moving items: click one to pick it up, click another slot to drop it there (swaps, or merges the same item).
public partial class InventoryWindow : CanvasLayer
{
    private const float SlotSize = 56f;

    private PanelContainer _window;
    private readonly Button[] _slots = new Button[Inventory.Size];
    private Label _stats, _spellStats;
    private Inventory _inventory;
    private int _held = -1;
    private float _statsTimer;

    public override void _Ready()
    {
        Layer = 10;
        AddToGroup("inventory_window");
        Build();
        _window.Visible = false;
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
        GameState.MenuOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
        RefreshStats();
    }

    private void Close()
    {
        _held = -1;
        _window.Visible = false;
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
        armour.AddChild(Placeholder("Head"));
        armour.AddChild(Placeholder("Chest"));
        armour.AddChild(Placeholder("Hands"));
        playerRow.AddChild(armour);

        var hands = new HBoxContainer();
        hands.AddChild(Placeholder("Focus"));
        hands.AddChild(Placeholder("Offhand"));
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
            int slot = i;
            var button = new Button { CustomMinimumSize = new Vector2(SlotSize, SlotSize), ClipText = true };
            button.Pressed += () => OnSlotPressed(slot);
            grid.AddChild(button);
            _slots[i] = button;
        }
    }

    // A slot that will hold gear later: visible so the layout is right, but does nothing yet
    private static Control Placeholder(string name) =>
        new Button { Text = name, Disabled = true, CustomMinimumSize = new Vector2(SlotSize + 20f, SlotSize) };

    // ---------- Filling it in ----------

    private void Refresh()
    {
        if (_inventory == null || !IsInstanceValid(_inventory)) return;

        for (int i = 0; i < Inventory.Size; i++)
        {
            Button button = _slots[i];
            ItemDefinition def = ItemDatabase.Get(_inventory.IdAt(i));
            int count = _inventory.CountAt(i);

            // Placeholder look: the item's tint and short name until icons are drawn
            button.Icon = def?.Icon;
            button.ExpandIcon = true;
            button.Text = def == null ? "" : def.Icon != null ? (count > 1 ? count.ToString() : "") : Short(def.DisplayName, count);
            button.TooltipText = def == null ? "" : $"{def.DisplayName}\n{def.Description}";
            button.SelfModulate = def == null ? Colors.White : def.Tint;
            button.Modulate = i == _held ? new Color(1f, 1f, 0.6f) : Colors.White;
        }
    }

    private static string Short(string name, int count)
    {
        string word = name.Split(' ')[0];
        if (word.Length > 6) word = word[..6];
        return count > 1 ? $"{word}\n{count}" : word;
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
        SpellDefinition spell = caster?.ActiveSpell;
        if (spell == null)
        {
            _spellStats.Text = "No spell equipped";
            return;
        }
        float range = caster.Stat(spell, nameof(SpellDefinition.Range));
        _spellStats.Text =
            $"{spell.SpellName}  ({spell.Element})\n" +
            $"Cost      {caster.CostOf(spell):0}\n" +
            $"Recharge  {caster.Stat(spell, nameof(SpellDefinition.RechargeTime)):0.0} s\n" +
            $"Range     {(range > 0f ? $"{range:0} m" : "unlimited")}";
    }

    // ---------- Moving items ----------

    private void OnSlotPressed(int slot)
    {
        if (_inventory == null) return;

        if (_held < 0)
        {
            if (!_inventory.IsEmpty(slot)) _held = slot;   // pick it up
        }
        else
        {
            if (slot != _held) _inventory.RequestMove(_held, slot);   // put it down
            _held = -1;
        }
        Refresh();
    }

    private PlayerController Me()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is PlayerController player && player.IsMultiplayerAuthority()) return player;
        return null;
    }
}
