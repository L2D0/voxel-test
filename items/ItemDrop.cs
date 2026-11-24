using Godot;

// An item lying in the world. The host decides who picks it up; every machine shows it bobbing and spinning.
// Placeholder look: a small cube in the item's tint until icons and models exist.
public partial class ItemDrop : Node3D
{
    private const float PickupRadius = 1.4f;
    public const float DefaultPickupDelay = 0.6f;   // so it can be seen landing before it's grabbed
    private const float Lifetime = 300f;      // gone after five minutes on the ground
    private const float CheckInterval = 0.1f;

    public string ItemId { get; private set; }
    public int Count { get; private set; }

    private float _age, _checkTimer;
    private float _pickupDelay = DefaultPickupDelay;   // longer for things a player threw away
    private Node3D _visual;

    public void Setup(string itemId, int count, float pickupDelay)
    {
        ItemId = itemId;
        Count = count;
        _pickupDelay = pickupDelay;
    }

    public override void _Ready()
    {
        ItemDefinition def = ItemDatabase.Get(ItemId);
        var material = new StandardMaterial3D { AlbedoColor = def?.Tint ?? Colors.Magenta };
        _visual = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One * 0.25f, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_visual);
        AddChild(new Label3D
        {
            Text = Count > 1 ? $"{def?.DisplayName ?? ItemId} x{Count}" : def?.DisplayName ?? ItemId,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0f, 0.55f, 0f),
            PixelSize = 0.003f,
            NoDepthTest = false,
        });
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _visual.Position = new Vector3(0f, 0.25f + Mathf.Sin(_age * 2.5f) * 0.06f, 0f);
        _visual.RotateY(dt * 1.5f);

        if (!Multiplayer.IsServer()) return;
        if (_age > Lifetime) { QueueFree(); return; }
        if (_age < _pickupDelay) return;

        _checkTimer -= dt;
        if (_checkTimer > 0f) return;
        _checkTimer = CheckInterval;

        foreach (Node node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not PlayerController player || player.IsDead) continue;
            if (player.GlobalPosition.DistanceTo(GlobalPosition) > PickupRadius) continue;
            if (Inventory.Of(player) is not Inventory inventory) continue;

            Count = inventory.Add(ItemId, Count);   // whatever didn't fit stays on the ground
            if (Count <= 0)
            {
                QueueFree();   // the host freeing it removes it everywhere
                return;
            }
        }
    }
}
