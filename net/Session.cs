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
        private bool _hostHooks;   // hooked into connections as host; undone on the way out
    private bool _leaving;
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

        var net = GetNode<NetworkManager>("/root/NetworkManager");
        net.SessionStarted += OnSessionStarted;
        net.SessionEnded += ReturnToMenu;   // the host went away, or never answered
    }

    private void OnSessionStarted(int seed)
    {
        // Host continuing a world: its edits go in before the first chunk is built
        WorldSave save = Multiplayer.IsServer() ? WorldSave.Current : null;
        if (save != null) World.LoadEdits(save.Edits);
        World.Initialize(seed);
        SendIdentity();
        if (save != null) StructureManager.Of(this)?.Load(save.Structures);   // host; the spawner sends them to everyone
        if (save != null) EnemySpawner.Of(this)?.Load(save.Enemies);

        if (!Multiplayer.IsServer()) return;   // only the host spawns players and sets the rules

        // Static, so set every session and an old value never carries over.
        // A new world takes the export; a loaded one keeps what it was saved with.
        Teams.FriendlyFire = save?.FriendlyFire ?? FriendlyFire;   // a new world carries the choice made on creation
        SpawnPlayer(1);
        RegisterPlayer(1, PlayerIdentity.Id);
        Multiplayer.PeerConnected += SpawnPlayer;
        Multiplayer.PeerConnected += SendRules;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        _hostHooks = true;
    }

    // Client: the session has started (the host sent the seed), so say who we are
    private void SendIdentity()
    {
        if (!Multiplayer.IsServer()) RpcId(1, MethodName.ReceiveIdentity, PlayerIdentity.Id);
    }

    // ---------- World rules ----------

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRules(bool friendlyFire) => Teams.FriendlyFire = friendlyFire;

    // Debug panel, host only: flip friendly fire for everyone
    public void ToggleFriendlyFire()
    {
        if (!Multiplayer.IsServer()) return;
        Teams.FriendlyFire = !Teams.FriendlyFire;
        Rpc(MethodName.ReceiveRules, Teams.FriendlyFire);
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

        SendWaystones(peer);

        if (WorldSave.Current == null || !WorldSave.Current.Players.TryGetValue(key, out WorldSave.PlayerSave data)) return;
        if (Players.GetNodeOrNull<PlayerController>(peer.ToString()) is not PlayerController player) return;

        bool alive = data.Health > 0f;
        if (alive) Health.Of(player)?.SetCurrent(data.Health);
        player.RestoreFromSave(alive, data.Position, data.Velocity, data.Skills);   // dead at save time: back at spawn, full health
    }

    private void CapturePlayer(long peer)
    {
        if (WorldSave.Current == null || !_keys.TryGetValue(peer, out string key)) return;
        if (Players.GetNodeOrNull<PlayerController>(peer.ToString()) is not PlayerController player) return;

        WorldSave.PlayerSave save = SaveOf(key);
        save.Position = player.GlobalPosition;
        save.Health = Health.Of(player)?.Current ?? 0f;
        save.Skills = player.Skills?.UnlockedIds() ?? System.Array.Empty<string>();

        // Your own machine knows your real velocity; for other players the host goes by how fast they're moving
        save.Velocity = player.IsMultiplayerAuthority() ? player.Velocity : player.ObservedVelocity;
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

    // ---------- Waystone lists ----------
    // Each player keeps their own list of waystones they've used, under names they chose.
    // The host owns the lists (they're saved with the player); each machine gets a copy of its own.

    // This machine's own list: structure id -> name
    public static Dictionary<string, string> LocalWaystones { get; private set; } = new();

    // Host: a player's save entry, made if they don't have one yet
    private static WorldSave.PlayerSave SaveOf(string key)
    {
        WorldSave.Current ??= WorldSave.CreateNew("default", 0, false);
        if (!WorldSave.Current.Players.TryGetValue(key, out WorldSave.PlayerSave save))
            WorldSave.Current.Players[key] = save = new WorldSave.PlayerSave();
        return save;
    }

    private void SendWaystones(long peer)
    {
        if (!_keys.TryGetValue(peer, out string key)) return;
        var list = SaveOf(key).Waystones;
        var ids = new string[list.Count];
        var names = new string[list.Count];
        int i = 0;
        foreach (var (id, name) in list) { ids[i] = id; names[i] = name; i++; }

        if (peer == 1) ReceiveWaystones(ids, names);
        else RpcId(peer, MethodName.ReceiveWaystones, ids, names);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveWaystones(string[] ids, string[] names)
    {
        LocalWaystones = new Dictionary<string, string>();
        for (int i = 0; i < ids.Length && i < names.Length; i++) LocalWaystones[ids[i]] = names[i];
    }

    // Host: does this player have this waystone on their list?
    public static bool Knows(Node player, string id)
    {
        string key = KeyOf(player);
        return key != "" && SaveOf(key).Waystones.ContainsKey(id);
    }

    // Host: add a waystone to a player's list under their name for it
    public static void Learn(Node player, string id, string name)
    {
        if (player?.GetTree().GetFirstNodeInGroup("session") is not Session session) return;
        string key = KeyOf(player);
        if (key == "" || !long.TryParse(player.Name, out long peer)) return;

        SaveOf(key).Waystones[id] = name;
        session.SendWaystones(peer);
    }

    // Host: a structure broke, so it comes off every list, online or not
    public static void ForgetStructure(Node any, string id)
    {
        if (WorldSave.Current == null || string.IsNullOrEmpty(id)) return;
        if (any?.GetTree().GetFirstNodeInGroup("session") is not Session session) return;

        foreach (var (key, save) in WorldSave.Current.Players)
        {
            if (!save.Waystones.Remove(id)) continue;
            foreach (var (peer, online) in session._keys)
                if (online == key) session.SendWaystones(peer);
        }
    }

    // Leaving keeps their character in the world for next time
    private void OnPeerDisconnected(long peer)
    {
        CapturePlayer(peer);
        _keys.Remove(peer);
        Players.GetNodeOrNull(peer.ToString())?.QueueFree();
    }

    // ---------- Leaving ----------

    private void SendRules(long peer) => RpcId(peer, MethodName.ReceiveRules, Teams.FriendlyFire);

    // Pause menu: the host saves first, then everyone goes back to the main menu
    public void QuitToMenu()
    {
        if (Multiplayer.IsServer()) SaveWorld();
        ReturnToMenu();
    }

    // Also runs on a client when the host goes away or can't be reached
    private void ReturnToMenu()
    {
        if (_leaving) return;
        _leaving = true;

        GetNode<NetworkManager>("/root/NetworkManager").Leave();

        // Shared state the session leaves behind, so the next one starts clean
        WorldSave.Current = null;
        LocalKey = "";
        LocalWaystones = new Dictionary<string, string>();
        Teams.FriendlyFire = false;
        GameState.MenuOpen = false;
        GameState.InGame = false;
        GameState.FreeCam = false;

        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);   // the menu is part of the main scene
    }

    public override void _ExitTree()
    {
        if (_hostHooks)
        {
            Multiplayer.PeerConnected -= SpawnPlayer;
            Multiplayer.PeerConnected -= SendRules;
            Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        }
        if (GetNodeOrNull<NetworkManager>("/root/NetworkManager") is NetworkManager net)
        {
            net.SessionStarted -= OnSessionStarted;
            net.SessionEnded -= ReturnToMenu;
        }
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
        save.Enemies = EnemySpawner.Of(this)?.Capture() ?? new List<WorldSave.EnemySave>();
        save.Save();
        GD.Print($"Saved world '{save.Name}': {save.Edits.Count} terrain edits, {save.Players.Count} players, {save.Structures.Count} structures, {save.Enemies.Count} enemies");
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