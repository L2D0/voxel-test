using Godot;
using System.Collections.Generic;

// Puts enemies into the world on every machine. The host decides what spawns and where;
// a MultiplayerSpawner then creates the same enemy, with the same name, everywhere, including
// for players who join later. For now the only way to spawn is the debug key.
public partial class EnemySpawner : Node
{
    [Export] public PackedScene EnemyScene;              // enemies/enemy.tscn
    [Export] public EnemyDefinition DebugEnemy;          // what F6 spawns
    [Export] public EnemyDefinition DebugEnemy2;         // what F7 spawns
    [Export] public int DebugCount = 3;
    [Export] public Key DebugKey = Key.F6;
    [Export] public Key DebugKey2 = Key.F7;

    private Node3D _enemies;
    private MultiplayerSpawner _spawner;
    private int _nextId;

    public static EnemySpawner Of(Node anyNode) =>
        anyNode.GetTree().GetFirstNodeInGroup("enemy_spawner") as EnemySpawner;

    public override void _Ready()
    {
        AddToGroup("enemy_spawner");
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

    // Host only. Returns the enemy so the caller can set it up further (packs).
    public Enemy Spawn(EnemyDefinition def, Vector3 position)
    {
        if (!Multiplayer.IsServer() || def == null || string.IsNullOrEmpty(def.ResourcePath)) return null;

        string name = $"Enemy_{_nextId++}";
        return _spawner.Spawn(new Godot.Collections.Array { def.ResourcePath, position, name }) as Enemy;
    }

    // ---------- Saving (host) ----------

    // Everything alive right now. Enemies only exist where players are (they despawn once no player is
    // near), so this is "what's near players" plus anything Persistent the host is keeping elsewhere.
    public List<WorldSave.EnemySave> Capture()
    {
        var saves = new List<WorldSave.EnemySave>();
        var packIds = new Dictionary<List<Enemy>, int>();

        foreach (Node node in _enemies.GetChildren())
        {
            if (node is not Enemy enemy || enemy.IsQueuedForDeletion() || enemy.Def == null) continue;
            if (Health.Of(enemy) is not Health health || health.IsDead) continue;   // corpses don't come back

            int pack = -1;
            if (enemy.Pack != null && !packIds.TryGetValue(enemy.Pack, out pack))
                packIds[enemy.Pack] = pack = packIds.Count;

            saves.Add(new WorldSave.EnemySave
            {
                Path = enemy.Def.ResourcePath,
                Position = enemy.GlobalPosition,
                Yaw = enemy.Rotation.Y,
                Velocity = enemy.Velocity,
                Health = health.Current,
                Pack = pack,
            });
        }
        return saves;
    }

    public void Load(List<WorldSave.EnemySave> saves)
    {
        var packs = new Dictionary<int, List<Enemy>>();

        foreach (WorldSave.EnemySave s in saves)
        {
            if (!ResourceLoader.Exists(s.Path))
            {
                GD.PushWarning($"Saved enemy '{s.Path}' no longer exists; skipped");
                continue;
            }
            if (Spawn(GD.Load<EnemyDefinition>(s.Path), s.Position) is not Enemy enemy) continue;

            enemy.Rotation = new Vector3(0f, s.Yaw, 0f);
            enemy.Velocity = s.Velocity;   // carries on once the ground under it is built
            Health.Of(enemy)?.SetCurrent(s.Health);

            if (s.Pack < 0) continue;
            if (!packs.TryGetValue(s.Pack, out List<Enemy> pack)) packs[s.Pack] = pack = new List<Enemy>();
            pack.Add(enemy);
        }

        foreach (List<Enemy> pack in packs.Values) Enemy.FormPack(pack);
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

    // Debug panel: what each slot spawns
    public string DebugName(int which) => DebugDef(which)?.DisplayName;

    // Debug panel: a group in front of whoever asked
    public void DebugSpawn(int which)
    {
        EnemyDefinition def = DebugDef(which);
        if (def == null || !GameState.InGame) return;

        Node3D me = FindLocalPlayer();
        if (me == null) return;

        Vector3 spot = me.GlobalPosition - me.GetNode<Node3D>("CameraPivot").GlobalBasis.Z * 6f + Vector3.Up * 2f;
        if (Multiplayer.IsServer()) SpawnGroup(def, spot);
        else RpcId(1, MethodName.RequestDebugSpawn, which, spot);
    }

    private EnemyDefinition DebugDef(int which) => which switch { 0 => DebugEnemy, 1 => DebugEnemy2, _ => null };

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDebugSpawn(int which, Vector3 spot)
    {
        if (Multiplayer.IsServer() && DebugDef(which) is EnemyDefinition def) SpawnGroup(def, spot);
    }

    private void SpawnGroup(EnemyDefinition def, Vector3 spot)
    {
        var group = new System.Collections.Generic.List<Enemy>();
        for (int i = 0; i < DebugCount; i++)
        {
            float angle = Mathf.Tau * i / DebugCount;
            if (Spawn(def, spot + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f) is Enemy enemy)
                group.Add(enemy);
        }
        if (def.PackAnimal) Enemy.FormPack(group);
    }

    private Node3D FindLocalPlayer()
    {
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Node3D player && player.IsMultiplayerAuthority()) return player;
        return null;
    }
}
