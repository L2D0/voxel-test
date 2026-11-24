using Godot;
using System.Collections.Generic;

// One world on disk: its seed, its rules, every terrain edit since it was made, every player who has
// played in it, every structure they've placed, and the creatures alive near players when it was saved.
// Only the host has one; clients get everything they need from the host when they join.
public class WorldSave
{
    private const uint Magic = 0x52575053;   // "SPWR" in the first four bytes, so we never read some other file
    private const int Version = 8;           // bump when the layout changes; old versions get read with their old layout
                                             // 1: seed, rules, edits.  2: + players.  3: + structures.
                                             // 4: + structure ids, players' waystone lists.
                                             // 5: + player velocity, enemies.  6: + player inventories.
                                             // 7: + hotbar binds.  8: + equipment, known spells, focus sockets

    public static WorldSave Current;         // the world being hosted right now, if any

    public string Name;
    public int Seed;
    public bool FriendlyFire;
    public bool IsNew = true;                // made this session, never loaded from disk
    public List<TerrainEdit> Edits = new();
    public Dictionary<string, PlayerSave> Players = new();   // by PlayerIdentity id, including players who are offline
    public List<StructureSave> Structures = new();            // oldest first
    public List<EnemySave> Enemies = new();

    // One creature that was alive when the world was saved
    public class EnemySave
    {
        public string Path;      // its EnemyDefinition
        public Vector3 Position;
        public float Yaw;
        public Vector3 Velocity; // mid-launch or mid-fall stays that way
        public float Health;
        public int Pack = -1;    // creatures with the same number were one pack; -1 = alone
    }

    // One placed structure
    public class StructureSave
    {
        public string Id;        // stays the same across saves; players' waystone lists point at it
        public string Path;      // its scene
        public Vector3 Position;
        public float Yaw;
        public string Owner;     // the saved-player key of whoever placed it
        public float Health;     // below 0 = full
    }

    // One player's character in this world
    public class PlayerSave
    {
        public string[] ItemIds = System.Array.Empty<string>();   // inventory, slot by slot ("" = empty)
        public int[] ItemCounts = System.Array.Empty<int>();
        public string[] Hotbar = System.Array.Empty<string>();      // "item:<id>", "spell:<path>" or ""
        public string[] Equipped = System.Array.Empty<string>();    // Focus, Offhand, Head, Chest, Hands ("" = empty)
        public string[] KnownSpells = System.Array.Empty<string>(); // every spell learned, by resource path
        public Dictionary<string, string[]> Sockets = new();        // focus item id -> spells socketed in it
        public Vector3 Position;
        public float Health;     // 0 = died before the save; comes back at spawn on full health
        public string[] Skills = System.Array.Empty<string>();
        public Dictionary<string, string> Waystones = new();   // structure id -> the name this player gave it
        public Vector3 Velocity;   // saved mid-fall means loaded mid-fall: no dodging fall damage by reloading
    }

    private static string FolderOf(string name) => $"user://worlds/{name}";
    private static string FileOf(string name) => $"{FolderOf(name)}/world.dat";

    public static bool Exists(string name) => FileAccess.FileExists(FileOf(name));

    public static WorldSave CreateNew(string name, int seed, bool friendlyFire) =>
        new() { Name = name, Seed = seed, FriendlyFire = friendlyFire };

    public static WorldSave Load(string name)
    {
        using FileAccess file = FileAccess.Open(FileOf(name), FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"Couldn't open world '{name}': {FileAccess.GetOpenError()}");
            return null;
        }
        if (file.Get32() != Magic)
        {
            GD.PushError($"World '{name}' isn't a Spellwright world file");
            return null;
        }

        int version = (int)file.Get32();
        if (version > Version)
        {
            GD.PushError($"World '{name}' was saved by a newer version (file {version}, game {Version})");
            return null;
        }

        var save = new WorldSave { Name = name, IsNew = false };
        save.Seed = (int)file.Get32();
        save.FriendlyFire = file.Get8() != 0;

        int count = (int)file.Get32();
        save.Edits.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            var point = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat());
            float radius = file.GetFloat();
            bool fill = file.Get8() != 0;
            save.Edits.Add(new TerrainEdit(point, radius, fill));
        }

        if (version >= 2)
        {
            int players = (int)file.Get32();
            for (int i = 0; i < players; i++)
            {
                string key = file.GetPascalString();
                var player = new PlayerSave
                {
                    Position = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat()),
                    Health = file.GetFloat(),
                };
                int skills = (int)file.Get32();
                player.Skills = new string[skills];
                for (int s = 0; s < skills; s++) player.Skills[s] = file.GetPascalString();
                if (version >= 4)
                {
                    int known = (int)file.Get32();
                    for (int w = 0; w < known; w++) player.Waystones[file.GetPascalString()] = file.GetPascalString();
                }
                if (version >= 5) player.Velocity = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat());
                if (version >= 6)
                {
                    int slots = (int)file.Get32();
                    player.ItemIds = new string[slots];
                    player.ItemCounts = new int[slots];
                    for (int s = 0; s < slots; s++)
                    {
                        player.ItemIds[s] = file.GetPascalString();
                        player.ItemCounts[s] = (int)file.Get32();
                    }
                }
                if (version >= 7)
                {
                    int binds = (int)file.Get32();
                    player.Hotbar = new string[binds];
                    for (int b = 0; b < binds; b++) player.Hotbar[b] = file.GetPascalString();
                }
                if (version >= 8)
                {
                    player.Equipped = ReadStrings(file);
                    player.KnownSpells = ReadStrings(file);
                    int foci = (int)file.Get32();
                    for (int f = 0; f < foci; f++) player.Sockets[file.GetPascalString()] = ReadStrings(file);
                }
                save.Players[key] = player;
            }
        }

        if (version >= 3)
        {
            int structures = (int)file.Get32();
            for (int i = 0; i < structures; i++)
                save.Structures.Add(new StructureSave
                {
                    Id = version >= 4 ? file.GetPascalString() : null,   // older saves get fresh ids on load
                    Path = file.GetPascalString(),
                    Position = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat()),
                    Yaw = file.GetFloat(),
                    Owner = file.GetPascalString(),
                    Health = file.GetFloat(),
                });
        }

        if (version >= 5)
        {
            int enemies = (int)file.Get32();
            for (int i = 0; i < enemies; i++)
                save.Enemies.Add(new EnemySave
                {
                    Path = file.GetPascalString(),
                    Position = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat()),
                    Yaw = file.GetFloat(),
                    Velocity = new Vector3(file.GetFloat(), file.GetFloat(), file.GetFloat()),
                    Health = file.GetFloat(),
                    Pack = (int)file.Get32(),
                });
        }
        return save;
    }

    // Writes to a temporary file first and swaps it in, so a crash mid-save never leaves a half-written world
    public void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute(FolderOf(Name));
        string temp = FileOf(Name) + ".tmp";

        using (FileAccess file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                GD.PushError($"Couldn't save world '{Name}': {FileAccess.GetOpenError()}");
                return;
            }

            file.Store32(Magic);
            file.Store32(Version);
            file.Store32((uint)Seed);
            file.Store8(FriendlyFire ? (byte)1 : (byte)0);

            file.Store32((uint)Edits.Count);
            foreach (TerrainEdit e in Edits)
            {
                file.StoreFloat(e.Point.X);
                file.StoreFloat(e.Point.Y);
                file.StoreFloat(e.Point.Z);
                file.StoreFloat(e.Radius);
                file.Store8(e.Fill ? (byte)1 : (byte)0);
            }

            file.Store32((uint)Players.Count);
            foreach (var (key, player) in Players)
            {
                file.StorePascalString(key);
                file.StoreFloat(player.Position.X);
                file.StoreFloat(player.Position.Y);
                file.StoreFloat(player.Position.Z);
                file.StoreFloat(player.Health);
                file.Store32((uint)player.Skills.Length);
                foreach (string skill in player.Skills) file.StorePascalString(skill);
                file.Store32((uint)player.Waystones.Count);
                foreach (var (id, name) in player.Waystones)
                {
                    file.StorePascalString(id);
                    file.StorePascalString(name);
                }
                file.StoreFloat(player.Velocity.X);
                file.StoreFloat(player.Velocity.Y);
                file.StoreFloat(player.Velocity.Z);
                file.Store32((uint)player.ItemIds.Length);
                for (int s = 0; s < player.ItemIds.Length; s++)
                {
                    file.StorePascalString(player.ItemIds[s] ?? "");
                    file.Store32((uint)(s < player.ItemCounts.Length ? player.ItemCounts[s] : 0));
                }
                file.Store32((uint)player.Hotbar.Length);
                foreach (string bind in player.Hotbar) file.StorePascalString(bind ?? "");
                WriteStrings(file, player.Equipped);
                WriteStrings(file, player.KnownSpells);
                file.Store32((uint)player.Sockets.Count);
                foreach (var (focus, spells) in player.Sockets)
                {
                    file.StorePascalString(focus);
                    WriteStrings(file, spells);
                }
            }

            file.Store32((uint)Structures.Count);
            foreach (StructureSave s in Structures)
            {
                file.StorePascalString(s.Id ?? "");
                file.StorePascalString(s.Path);
                file.StoreFloat(s.Position.X);
                file.StoreFloat(s.Position.Y);
                file.StoreFloat(s.Position.Z);
                file.StoreFloat(s.Yaw);
                file.StorePascalString(s.Owner ?? "");
                file.StoreFloat(s.Health);
            }

            file.Store32((uint)Enemies.Count);
            foreach (EnemySave e in Enemies)
            {
                file.StorePascalString(e.Path);
                file.StoreFloat(e.Position.X);
                file.StoreFloat(e.Position.Y);
                file.StoreFloat(e.Position.Z);
                file.StoreFloat(e.Yaw);
                file.StoreFloat(e.Velocity.X);
                file.StoreFloat(e.Velocity.Y);
                file.StoreFloat(e.Velocity.Z);
                file.StoreFloat(e.Health);
                file.Store32((uint)e.Pack);
            }
        }

        DirAccess.RemoveAbsolute(FileOf(Name));   // Windows won't rename over an existing file
        Error err = DirAccess.RenameAbsolute(temp, FileOf(Name));
        if (err != Error.Ok) GD.PushError($"Couldn't finish saving world '{Name}': {err}");
        IsNew = false;
    }

    private static string[] ReadStrings(FileAccess file)
    {
        var list = new string[(int)file.Get32()];
        for (int i = 0; i < list.Length; i++) list[i] = file.GetPascalString();
        return list;
    }

    private static void WriteStrings(FileAccess file, string[] list)
    {
        list ??= System.Array.Empty<string>();
        file.Store32((uint)list.Length);
        foreach (string s in list) file.StorePascalString(s ?? "");
    }

    // Throws the whole world away: its file, any half-finished save, and its folder
    public static void Delete(string name)
    {
        DirAccess.RemoveAbsolute(FileOf(name));
        DirAccess.RemoveAbsolute(FileOf(name) + ".tmp");
        DirAccess.RemoveAbsolute(FolderOf(name));
    }

    // Every saved world, most recently played first
    public static List<(string Name, ulong Modified)> ListWorlds()
    {
        var worlds = new List<(string Name, ulong Modified)>();
        using DirAccess dir = DirAccess.Open("user://worlds");
        if (dir == null) return worlds;

        foreach (string name in dir.GetDirectories())
            if (Exists(name)) worlds.Add((name, FileAccess.GetModifiedTime(FileOf(name))));
        worlds.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return worlds;
    }

    // A world's name is its folder name, so keep it to characters every OS accepts
    public static string CleanName(string name)
    {
        var clean = new System.Text.StringBuilder();
        foreach (char c in (name ?? "").StripEdges())
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') clean.Append(c);
        string result = clean.ToString().StripEdges();
        if (result.Length > 32) result = result[..32];
        return result == "" ? "New World" : result;
    }
}
