using Godot;

// Who this machine's player is, across sessions. A random id made the first time the game runs
// and kept in user data. The host files each player's save under it, so rejoining gets your character back.
//
// Two game windows on one computer share user data, so they share an id. The host tells them apart
// by joining order (the second gets "#2"), which is fine for testing.
public static class PlayerIdentity
{
    private const string File = "user://player_id.txt";
    private static string _id;

    public static string Id
    {
        get
        {
            if (_id != null) return _id;

            if (FileAccess.FileExists(File))
            {
                using FileAccess read = FileAccess.Open(File, FileAccess.ModeFlags.Read);
                _id = read?.GetLine().StripEdges();
            }
            if (string.IsNullOrEmpty(_id))
            {
                _id = System.Guid.NewGuid().ToString("N");
                using FileAccess write = FileAccess.Open(File, FileAccess.ModeFlags.Write);
                write?.StoreLine(_id);
            }
            return _id;
        }
    }
}
