using Godot;

// Something a player places that stays in the world and is saved with it: waystones now, and later the
// mother tree, druid portals and buildings. The host creates it through StructureManager and every machine
// gets a copy, including players who join later. Anyone can damage one; it breaks at 0 HP.
public partial class Structure : StaticBody3D
{
    [Export] public string DisplayName = "Structure";
    [Export] public int MaxPerOwner = 0;   // 0 = no cap. At the cap, placing another breaks the oldest.

    public string OwnerKey { get; private set; } = "";   // the saved-player key of whoever placed it
    public long Order { get; private set; }               // placement order; lower is older
    public string ScenePath { get; private set; } = "";

    private Health _health;
    private Label3D _label;

    // Every machine, before it enters the tree
    public void Setup(string scenePath, string ownerKey, long order)
    {
        ScenePath = scenePath;
        OwnerKey = ownerKey;
        Order = order;
    }

    public override void _Ready()
    {
        AddToGroup("structures");
        _health = Health.Of(this);
        _label = GetNodeOrNull<Label3D>("Label");

        if (_health != null)
        {
            _health.Changed += (current, max) => UpdateLabel();
            _health.Died += source => Break();
        }
        UpdateLabel();
    }

    // Host only. Freeing a node the spawner made removes it on every machine.
    public void Break()
    {
        if (Multiplayer.IsServer() && !IsQueuedForDeletion()) QueueFree();
    }

    private void UpdateLabel()
    {
        if (_label == null) return;
        _label.Text = _health != null ? $"{DisplayName}\n{_health.Current:0} / {_health.Max:0}" : DisplayName;
    }
}
