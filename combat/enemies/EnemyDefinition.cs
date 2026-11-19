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
    [Export] public Godot.Collections.Array<string> Tags = new();   // what others see it as: "Predator", "Prey"...
    [Export] public bool Persistent = false;                 // livestock, pets, mounts: always saved
    [Export] public Color Tint = new(0.55f, 0.35f, 0.3f);    // placeholder body colour until models exist

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed = 3.5f;
    [Export] public float ChaseSpeedScale = 1.6f;            // chasing and fleeing run faster than walking
    [Export] public float Acceleration = 10f;
    [Export] public float TurnSpeed = 8f;
    [Export] public float JumpVelocity = 5.5f;               // hops up a step when it walks into one

    [ExportGroup("Stamina")]
    [Export] public float MaxStamina = 12f;                  // seconds of running faster than walking; 0 = never tires
    [Export] public float StaminaRegen = 1f;                 // seconds of stamina back per second of not running
    [Export] public float TiredSpeedScale = 0.8f;            // speed cap once it runs dry, until stamina is full again

    [ExportGroup("Senses")]
    [Export] public float SightRange = 20f;
    [Export(PropertyHint.Range, "0,360")] public float SightAngle = 120f;   // the whole cone, not half
    [Export] public float CloseSense = 2.5f;                 // inside this it notices you behind its back too
    [Export] public float NoticeTime = 0.8f;                 // seconds of being seen at mid range before it reacts
    [Export] public float HearingScale = 1f;                 // multiplies every noise radius; 0 = deaf

    [ExportGroup("Behaviour")]
    [Export] public Godot.Collections.Array<TagResponse> Responses = new();
    [Export(PropertyHint.Range, "0,1")] public float FleeHealth = 0f;   // runs below this fraction of HP; 0 = never
    [Export] public float StopDistance = 1.8f;               // how close it gets to a target when it has no abilities
    [Export] public float AvoidDistance = 8f;                // Avoid keeps this far away
    [Export] public float PackCallRadius = 15f;              // same-kind enemies inside this join the fight
    [Export] public float WanderRadius = 12f;                // how far one roaming leg goes (anchored: how far from home)
    [Export] public float LeashRadius = 40f;                 // gives up past this from where the chase began (anchored: from home)
    [Export] public float SweepRadius = 8f;                  // how widely it searches around the last seen spot
    [Export] public float SweepTime = 10f;                   // how long it searches before going home
    [Export] public float ThreatMemory = 10f;                // seconds for threat to fade to about a third

    [ExportGroup("Abilities")]
    [Export] public Godot.Collections.Array<EnemyAbility> Abilities = new();

    [ExportGroup("Roaming")]
    [Export] public bool Anchored = false;                   // guards, nest keepers: stay around their spawn and walk back to it
    [Export] public bool PackAnimal = false;                 // a group spawned together sticks together and follows a leader
    [Export] public float PackSpread = 5f;                   // how far followers stray from the leader
    [Export] public float CalmTime = 5f;                     // after giving up a chase, how long it ignores everything

    [ExportGroup("Falling")]
    [Export] public float SafeFallSpeed = 12f;
    [Export] public float LethalFallSpeed = 30f;             // a fall this fast takes all its max health
    [Export] public float TerminalVelocity = 55f;

    [ExportGroup("Death")]
    [Export] public float CorpseTime = 4f;                   // how long the body lies there before it goes

    [ExportGroup("Spawning and loot")]
    [Export] public Godot.Collections.Array<SpawnRule> SpawnRules = new();
    [Export] public Godot.Collections.Array<LootEntry> Loot = new();
}
