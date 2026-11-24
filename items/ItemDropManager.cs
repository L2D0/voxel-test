using Godot;

// Puts item drops into the world on every machine. Same pattern as EnemySpawner: the host decides, a
// MultiplayerSpawner copies it to everyone, including players who join later. Drops aren't saved.
public partial class ItemDropManager : Node
{
    private Node3D _root;
    private MultiplayerSpawner _spawner;
    private int _nextId;

    public static ItemDropManager Of(Node anyNode) =>
        anyNode.GetTree().GetFirstNodeInGroup("item_drops") as ItemDropManager;

    public override void _Ready()
    {
        AddToGroup("item_drops");
        _root = new Node3D { Name = "ItemDrops" };
        AddChild(_root);
        _spawner = new MultiplayerSpawner { Name = "Spawner" };
        AddChild(_spawner);
        _spawner.SpawnPath = _spawner.GetPathTo(_root);
        _spawner.SpawnFunction = Callable.From<Variant, Node>(CreateDrop);
    }

    // Host only. Lands it on the ground below the point, with a little scatter so piles spread out.
    public void Drop(string itemId, int count, Vector3 at, float pickupDelay = ItemDrop.DefaultPickupDelay)
    {
        if (!Multiplayer.IsServer() || count <= 0 || ItemDatabase.Get(itemId) == null) return;

        at += new Vector3((float)GD.RandRange(-0.4, 0.4), 0f, (float)GD.RandRange(-0.4, 0.4));
        var ray = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.5f, at + Vector3.Down * 8f,
                                                     SpellFx.WorldLayer | SpellFx.StructureLayer);
        var hit = GetViewport().World3D.DirectSpaceState.IntersectRay(ray);
        if (hit.Count > 0) at = (Vector3)hit["position"];

        _spawner.Spawn(new Godot.Collections.Array { itemId, count, at, $"Drop_{_nextId++}", pickupDelay });
    }

    private Node CreateDrop(Variant data)
    {
        var args = data.AsGodotArray();
        var drop = new ItemDrop { Name = args[3].AsString(), Position = args[2].AsVector3() };
        drop.Setup(args[0].AsString(), args[1].AsInt32(), args[4].AsSingle());
        return drop;
    }
}
