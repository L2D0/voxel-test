using Godot;
using System.Collections.Generic;

// Puts structures into the world on every machine, enforces each player's caps, and hands them to the
// world save. Same pattern as EnemySpawner: the host decides, a MultiplayerSpawner copies it everywhere.
public partial class StructureManager : Node
{
    private Node3D _root;
    private MultiplayerSpawner _spawner;
    private long _nextOrder;

    public static StructureManager Of(Node anyNode) =>
        anyNode.GetTree().GetFirstNodeInGroup("structure_manager") as StructureManager;

    public override void _Ready()
    {
        AddToGroup("structure_manager");

        // Built in code so there's nothing to wire up in the editor
        _root = new Node3D { Name = "Structures" };
        AddChild(_root);
        _spawner = new MultiplayerSpawner { Name = "Spawner" };
        AddChild(_spawner);
        _spawner.SpawnPath = _spawner.GetPathTo(_root);
        _spawner.SpawnFunction = Callable.From<Variant, Node>(CreateStructure);
    }

    // ---------- Placing (host) ----------

    // health: a saved value to restore, or below 0 for full. id: a saved id to keep, or null for a new one.
    public Structure Place(PackedScene scene, Vector3 position, float yaw, string ownerKey, float health = -1f, string id = null)
    {
        if (!Multiplayer.IsServer() || scene == null || string.IsNullOrEmpty(scene.ResourcePath)) return null;

        long order = _nextOrder++;
        if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
        var structure = _spawner.Spawn(new Godot.Collections.Array
        {
            scene.ResourcePath, position, yaw, ownerKey ?? "", order, id,
        }) as Structure;
        if (structure == null) return null;

        // Over the cap: the oldest of the same kind, same owner, breaks
        if (structure.MaxPerOwner > 0)
        {
            List<Structure> others = OwnedBy(structure.ScenePath, structure.OwnerKey);
            others.Remove(structure);
            while (others.Count >= structure.MaxPerOwner)
            {
                others[0].Break();
                others.RemoveAt(0);
            }
        }

        if (health >= 0f) Health.Of(structure)?.SetCurrent(health);
        return structure;
    }

    // Every structure of one kind that one player owns, oldest first
    public List<Structure> OwnedBy(string scenePath, string ownerKey)
    {
        var list = new List<Structure>();
        foreach (Node node in _root.GetChildren())
            if (node is Structure s && !s.IsQueuedForDeletion() && s.ScenePath == scenePath && s.OwnerKey == ownerKey)
                list.Add(s);
        list.Sort((a, b) => a.Order.CompareTo(b.Order));
        return list;
    }

    public Structure FindById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Node node in _root.GetChildren())
            if (node is Structure s && s.Id == id && !s.IsQueuedForDeletion()) return s;
        return null;
    }

    // Every machine: build the structure from what the host sent
    private Node CreateStructure(Variant data)
    {
        var args = data.AsGodotArray();
        string path = args[0].AsString();
        long order = args[4].AsInt64();

        var structure = GD.Load<PackedScene>(path).Instantiate<Structure>();
        structure.Name = $"Structure_{order}";
        structure.Position = args[1].AsVector3();
        structure.Rotation = new Vector3(0f, args[2].AsSingle(), 0f);
        structure.Setup(args[5].AsString(), path, args[3].AsString(), order);
        return structure;   // the spawner adds it under Structures itself
    }

    // ---------- Saving (host) ----------

    public List<WorldSave.StructureSave> Capture()
    {
        var all = new List<Structure>();
        foreach (Node node in _root.GetChildren())
            if (node is Structure s && !s.IsQueuedForDeletion()) all.Add(s);
        all.Sort((a, b) => a.Order.CompareTo(b.Order));   // saved oldest first, so "oldest" survives a reload

        var saves = new List<WorldSave.StructureSave>();
        foreach (Structure s in all)
            saves.Add(new WorldSave.StructureSave
            {
                Id = s.Id,
                Path = s.ScenePath,
                Position = s.GlobalPosition,
                Yaw = s.Rotation.Y,
                Owner = s.OwnerKey,
                Health = Health.Of(s)?.Current ?? -1f,
            });
        return saves;
    }

    public void Load(List<WorldSave.StructureSave> saves)
    {
        foreach (WorldSave.StructureSave s in saves)
        {
            if (!ResourceLoader.Exists(s.Path))
            {
                GD.PushWarning($"Saved structure '{s.Path}' no longer exists; skipped");
                continue;
            }
            Place(GD.Load<PackedScene>(s.Path), s.Position, s.Yaw, s.Owner, s.Health, s.Id);
        }
    }
}
