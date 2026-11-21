using Godot;
using System.Collections.Generic;

public partial class Session : Node
{
    [Export] public PackedScene PlayerScene;
    [Export] public VoxelWorld World;
    [Export] public Node3D Players;
    [Export] public MultiplayerSpawner Spawner;
    [Export] public Vector3 SpawnPoint = new Vector3(0, 70, 0);
    [Export] public bool FriendlyFire = false;   // world rule; chosen when creating a world once the main menu exists (4.7)
    [Export] public Key FriendlyFireDebugKey = Key.F9;   // host only: flips it mid-game for testing (F8 is the editor's stop key)
    [Export] public float AutosaveInterval = 300f;       // seconds; the host also saves when the game closes

    private float _autosaveTimer;
    private readonly Dictionary<long, string> _keys = new();   // host: peer id -> the key their save is filed under

    public override void _Ready()
    {
        if (PlayerScene == null || World == null || Players == null || Spawner == null)
        {
            GD.PushError("Session is missing PlayerScene, World, Players or Spawner");
            return;
        }

        // Runs on every machine whenever the host calls Spawner.Spawn(...)
        Spawner.SpawnFunction = Callable.From<Variant, Node>(CreatePlayer);
        AddToGroup("session");   // the pause menu's Save button finds it here

        GetNode<NetworkManager>("/root/NetworkManager").SessionStarted += OnSessionStarted;
    }

    private void OnSessionStarted(int seed)
    {
        // Host continuing a world: its edits go in before the first chunk is built
        WorldSave save = Multiplayer.IsServer() ? WorldSave.Current : null;
        if (save != null) World.LoadEdits(save.Edits);
        World.Initialize(seed);
        SendIdentity();
        if (save != null) StructureManager.Of(this)?.Load(save.Structures);   // host; the spawner sends them to everyone

        if (!Multiplayer.IsServer()) return;   // only the host spawns players and sets the rules

        // Static, so set every session and an old value never carries over.
        // A new world takes the export; a loaded one keeps what it was saved with.
        Teams.FriendlyFire = save == null || save.IsNew ? FriendlyFire : save.FriendlyFire;
        SpawnPlayer(1);
        RegisterPlayer(1, PlayerIdentity.Id);
        Multiplayer.PeerConnected += SpawnPlayer;
        Multiplayer.PeerConnected += id => RpcId(id, MethodName.ReceiveRules, Teams.FriendlyFire);
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    // Client: the session has started (the host sent the seed), so say who we are
    private void SendIdentity()
    {
        if (!Multiplayer.IsServer()) RpcId(1, MethodName.ReceiveIdentity, PlayerIdentity.Id);
    }

    // ---------- World rules ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRules(bool friendlyFire) => Teams.FriendlyFire = friendlyFire;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != FriendlyFireDebugKey) return;
        if (!Multiplayer.IsServer() || !GameState.InGame) return;

        Teams.FriendlyFire = !Teams.FriendlyFire;
        Rpc(MethodName.ReceiveRules, Teams.FriendlyFire);
        GD.Print($"Friendly fire {(Teams.FriendlyFire ? "on" : "off")}");
    }

    // ---------- Players' saves (host) ----------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveIdentity(string id)
    {
        if (Multiplayer.IsServer()) RegisterPlayer(Multiplayer.GetRemoteSenderId(), id);
    }

    // Files a peer under its identity, then gives back their character if this world has one
    private void RegisterPlayer(long peer, string id)
    {
        string key = id;
        for (int n = 2; _keys.ContainsValue(key); n++) key = $"{id}#{n}";   // two windows on one computer
        _keys[peer] = key;
        if (peer == 1) LocalKey = key;
        else RpcId(peer, MethodName.ReceiveKey, key);   // so the player knows which waystones are theirs

        if (WorldSave.Current == null || !WorldSave.Current.Players.TryGetValue(key, out WorldSave.PlayerSave data)) return;
        if (Players.GetNodeOrNull<PlayerController>(peer.ToString()) is not PlayerController player) return;

        bool alive = data.Health > 0f;
        if (alive) Health.Of(player)?.SetCurrent(data.Health);
        player.RestoreFromSave(alive, data.Position, data.Skills);   // dead at save time: back at spawn, full health
    }

    private void CapturePlayer(long peer)
    {
        if (WorldSave.Current == null || !_keys.TryGetValue(peer, out string key)) return;
        if (Players.GetNodeOrNull<PlayerController>(peer.ToString()) is not PlayerController player) return;

        WorldSave.Current.Players[key] = new WorldSave.PlayerSave
        {
            Position = player.GlobalPosition,
            Health = Health.Of(player)?.Current ?? 0f,
            Skills = player.Skills?.UnlockedIds() ?? System.Array.Empty<string>(),
        };
    }

    // The saved-player key of a player node (named after its peer id). Host only; "" if unknown.
    public static string KeyOf(Node player)
    {
        if (player?.GetTree().GetFirstNodeInGroup("session") is not Session session) return "";
        if (!long.TryParse(player.Name, out long peer)) return "";
        return session._keys.TryGetValue(peer, out string key) ? key : "";
    }

    // This machine's own saved-player key, as the host filed it (two windows on one computer get "#2")
    public static string LocalKey { get; private set; } = "";

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveKey(string key) => LocalKey = key;

    // Leaving keeps their character in the world for next time
    private void OnPeerDisconnected(long peer)
    {
        CapturePlayer(peer);
        _keys.Remove(peer);
        Players.GetNodeOrNull(peer.ToString())?.QueueFree();
    }

    // ---------- Saving (host) ----------

    public override void _Process(double delta)
    {
        if (WorldSave.Current == null || !GameState.InGame || !Multiplayer.IsServer()) return;

        _autosaveTimer += (float)delta;
        if (_autosaveTimer < AutosaveInterval) return;
        _autosaveTimer = 0f;
        SaveWorld();
    }

    // Closing the window, or Quit in the pause menu
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveWorld();
    }

    public void SaveWorld()
    {
        WorldSave save = WorldSave.Current;
        if (save == null) { GD.Print("Not saving: no world loaded (joined as a client, or the menu's Host wasn't used)"); return; }
        if (!Multiplayer.IsServer()) { GD.Print("Not saving: only the host saves"); return; }
        if (!GameState.InGame) { GD.Print("Not saving: not in game yet"); return; }

        save.Seed = World.Seed;
        save.FriendlyFire = Teams.FriendlyFire;
        save.Edits = new List<TerrainEdit>(World.EditLog);
        foreach (long peer in _keys.Keys) CapturePlayer(peer);   // offline players keep what they left with
        save.Structures = StructureManager.Of(this)?.Capture() ?? new List<WorldSave.StructureSave>();
        save.Save();
        GD.Print($"Saved world '{save.Name}': {save.Edits.Count} terrain edits, {save.Players.Count} players, {save.Structures.Count} structures");
    }

    // ---------- Players ----------

    // Host only: decide the spawn data and broadcast it
    private void SpawnPlayer(long id)
    {
        Vector3 position = SpawnPoint + new Vector3((id % 8) * 2f, 0, 0);
        Spawner.Spawn(new Godot.Collections.Array { id, position });
    }

    // Every machine: build the player from the broadcast data
    private Node CreatePlayer(Variant data)
    {
        var args = data.AsGodotArray();
        var player = PlayerScene.Instantiate<PlayerController>();
        player.Name = args[0].AsInt64().ToString();   // the node's name *is* the owning peer's id
        player.Position = args[1].AsVector3();
        return player;   // the spawner adds it under Players itself
    }
}