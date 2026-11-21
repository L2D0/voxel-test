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

        if (!Multiplayer.IsServer()) return;   // only the host spawns players and sets the rules

        // Static, so set every session and an old value never carries over.
        // A new world takes the export; a loaded one keeps what it was saved with.
        Teams.FriendlyFire = save == null || save.IsNew ? FriendlyFire : save.FriendlyFire;
        SpawnPlayer(1);
        Multiplayer.PeerConnected += SpawnPlayer;
        Multiplayer.PeerConnected += id => RpcId(id, MethodName.ReceiveRules, Teams.FriendlyFire);
        Multiplayer.PeerDisconnected += id => Players.GetNodeOrNull(id.ToString())?.QueueFree();
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
        save.Save();
        GD.Print($"Saved world '{save.Name}': {save.Edits.Count} terrain edits");
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