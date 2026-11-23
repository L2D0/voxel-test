using Godot;
using System.Collections.Generic;

// Every ItemDefinition in items/defs/, by Id. Loaded the first time anything asks.
public static class ItemDatabase
{
    private const string Folder = "res://items/defs";
    private static Dictionary<string, ItemDefinition> _items;

    public static ItemDefinition Get(string id)
    {
        Load();
        return id != null && _items.TryGetValue(id, out ItemDefinition def) ? def : null;
    }

    public static IEnumerable<ItemDefinition> All
    {
        get { Load(); return _items.Values; }
    }

    public static int MaxStackOf(string id) => Mathf.Max(1, Get(id)?.MaxStack ?? 1);

    private static void Load()
    {
        if (_items != null) return;
        _items = new Dictionary<string, ItemDefinition>();

        using DirAccess dir = DirAccess.Open(Folder);
        if (dir == null)
        {
            GD.PushError($"No item folder at {Folder}");
            return;
        }
        foreach (string file in dir.GetFiles())
        {
            // An exported game renames resources to .tres.remap; loading the plain name still works
            string name = file.TrimSuffix(".remap");
            if (!name.EndsWith(".tres")) continue;

            if (GD.Load($"{Folder}/{name}") is not ItemDefinition def) continue;
            if (string.IsNullOrEmpty(def.Id)) GD.PushWarning($"{name} has no Id; skipped");
            else if (_items.ContainsKey(def.Id)) GD.PushWarning($"Two items share the Id '{def.Id}'; {name} skipped");
            else _items[def.Id] = def;
        }
    }
}
