using Godot;

public enum EffectShape { Pool, Cloud }

[GlobalClass]
public partial class GroundEffectDefinition : Resource
{
    [Export] public string DisplayName = "Effect";
    [Export] public EffectShape Shape = EffectShape.Pool;
    [Export] public float Radius = 3f;
    [Export] public float Height = 2f;             // pools: how far above the ground they reach
    [Export] public float Duration = 5f;
    [Export] public float TickInterval = 0.5f;
    [Export] public float MaxDropDistance = 6f;    // pools: how far down they look for ground
    [Export] public Color Color = new(1f, 0.5f, 0.2f, 0.35f);
    [Export] public Godot.Collections.Array<string> Tags = new();

    [ExportGroup("Growth")]
    [Export] public float GrowthPerSecond = 0f;
    [Export] public float MaxRadius = 0f;          // 0 = no cap

    [ExportGroup("Enemies")]
    [Export] public float Damage = 0f;             // per tick
    [Export] public DamageType DamageType = DamageType.Physical;
    [Export] public bool CanKill = true;
    [Export] public Godot.Collections.Array<StatusDefinition> EnemyStatuses = new();
    [Export] public bool HarmsCaster = false;      // the caster takes the enemy side too

    [ExportGroup("Allies")]
    [Export] public Godot.Collections.Array<StatusDefinition> AllyStatuses = new();
    [Export] public Godot.Collections.Array<StatModifier> AllyModifiers = new();   // while inside
}