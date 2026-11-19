using Godot;

// Puts enemies into the world on every machine. The host decides what spawns and where;
// a MultiplayerSpawner then creates the same enemy, with the same name, everywhere, including
// for players who join later. For now the only way to spawn is the debug key.
public partial class EnemySpawner : Node
{
    [Export] public PackedScene EnemyScene;              // enemies/enemy.tscn
    [Export] public EnemyDefinition DebugEnemy;          // what the debug key spawns
    [Export] public int DebugCount = 3;
    [Export] public Key DebugKey = Key.F6;

    private Node3D _enemies;
    private MultiplayerSpawner _spawner;
    private int _nextId;

    public override void _Ready()
    {
        if (EnemyScene == null)
        {
            GD.PushError("EnemySpawner has no EnemyScene");
            return;
        }

        // Built in code so there's nothing to wire up in the editor. Same names on every machine.
        _enemies = new Node3D { Name = "Enemies" };
        AddChild(_enemies);

        _spawner = new MultiplayerSpawner { Name = "Spawner" };
        AddChild(_spawner);
        _spawner.SpawnPath = _spawner.GetPathTo(_enemies);
        _spawner.SpawnFunction = Callable.From<Variant, Node>(CreateEnemy);
    }

    // ---------- Spawning ----------

    // Host only
    public void Spawn(EnemyDefinition def, Vector3 position)
    {
        if (!Multiplayer.IsServer() || def == null || string.IsNullOrEmpty(def.ResourcePath)) return;

        string name = $"Enemy_{_nextId++}";
        _spawner.Spawn(new Godot.Collections.Array { def.ResourcePath, position, name });
    }

    // Every machine: build the enemy from what the host sent
    private Node CreateEnemy(Variant data)
    {
        var args = data.AsGodotArray();
        var def = GD.Load<EnemyDefinition>(args[0].AsString());

        var enemy = EnemyScene.Instantiate<Enemy>();
        enemy.Name = args[2].AsString();
        enemy.Position = args[1].AsVector3();
        enemy.Setup(def);
        return enemy;   // the spawner adds it under Enemies itself
    }

    // ---------- Debug ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != DebugKey) return;
        if (DebugEnemy == null || !GameState.InGame) return;

        // Spawn in front of whoever pressed it
        Node3D me = FindLocalPlayer();
        if (me == null) return;

        Vector3 spot = me.GlobalPosition - me.GetNode<Node3D>("CameraPivot").GlobalBasis.Z * 6f + Vector3.Up * 2f;
        if (Multiplayer.IsServer()) SpawnGroup(spot);
        else RpcId(1, MethodName.RequestDebugSpawn, spot);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDebugSpawn(Vector3 spot)
    {
        if (Multiplayer.IsServer()) SpawnGroup(spot);
    }

    private void SpawnGroup(Vector3 spot)
    {
        for (int i = 0; i < DebugCount; i++)
        {
            float angle = Mathf.Tau * i / DebugCount;
            Spawn(DebugEnemy, spot + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f);
        }
    }

    private Node3D FindLocalPlayer()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Node3D player && player.IsMultiplayerAuthority()) return player;
        return null;
    }
}
