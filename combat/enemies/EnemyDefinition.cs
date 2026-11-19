using Godot;

// Everything that makes one kind of enemy different from another. A moose and a spider are two
// of these files running the same Enemy code.
[GlobalClass]
public partial class EnemyDefinition : Resource
{
    [Export] public string DisplayName = "Enemy";
    [Export] public float MaxHealth = 60f;
    [Export] public Resistances Resistances;                 // optional; share one .tres per kind
    [Export] public WeightClass Weight = WeightClass.Medium;
    [Export] public Godot.Collections.Array<string> Tags = new();   // "Predator", "Prey", "Insect"... (part 2)
    [Export] public bool Persistent = false;                 // livestock, pets, mounts: always saved
    [Export] public Color Tint = new(0.55f, 0.35f, 0.3f);    // placeholder body colour until models exist

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed = 3.5f;
    [Export] public float Acceleration = 10f;
    [Export] public float TurnSpeed = 8f;

    [ExportGroup("Falling")]
    [Export] public float SafeFallSpeed = 12f;
    [Export] public float LethalFallSpeed = 30f;             // a fall this fast takes all its max health

    [ExportGroup("Death")]
    [Export] public float CorpseTime = 4f;                   // how long the body lies there before it goes

    [ExportGroup("Spawning and loot")]
    [Export] public Godot.Collections.Array<SpawnRule> SpawnRules = new();
    [Export] public Godot.Collections.Array<LootEntry> Loot = new();
}
