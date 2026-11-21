using Godot;
using System.Collections.Generic;

// One world on disk: its seed, its rules and every terrain edit since it was made.
// Placed structures, enemies and players join it later (pipeline 4.2 to 4.4).
// Only the host has one; clients get everything they need from the host when they join.
public class WorldSave
{
    private const uint Magic = 0x52575053;   // "SPWR" in the first four bytes, so we never read some other file
    private const int Version = 1;           // bump when the layout changes; old versions get read with their old layout

    public static WorldSave Current;         // the world being hosted right now, if any

    public string Name;
    public int Seed;
    public bool FriendlyFire;
    public bool IsNew = true;                // made this session, never loaded from disk
    public List<TerrainEdit> Edits = new();

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
        }

        DirAccess.RemoveAbsolute(FileOf(Name));   // Windows won't rename over an existing file
        Error err = DirAccess.RenameAbsolute(temp, FileOf(Name));
        if (err != Error.Ok) GD.PushError($"Couldn't finish saving world '{Name}': {err}");
        IsNew = false;
    }

    public static void Delete(string name)
    {
        DirAccess.RemoveAbsolute(FileOf(name));
    }
}
