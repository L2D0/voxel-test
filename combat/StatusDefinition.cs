using Godot;

public enum StatusCategory { Buff, Debuff, Neutral }
public enum StackMode { AllAtOnce, OneAtATime }

[GlobalClass]
public partial class StatusDefinition : Resource
{
    [Export] public string DisplayName = "Status";
    [Export] public StatusCategory Category = StatusCategory.Debuff;
    [Export] public float Duration = 3f;
    [Export] public int MaxStacks = 1;
    [Export] public StackMode StackMode = StackMode.AllAtOnce;

    [ExportGroup("Damage over time")]
    [Export] public float TickInterval = 1f;
    [Export] public float TickDamagePerStack = 0f;   // 0 = doesn't tick
    [Export] public DamageType TickType = DamageType.Physical;
    [Export] public bool TickCanKill = true;         // false for Poison and Leach

    [ExportGroup("Amplification")]
    [Export] public Godot.Collections.Array<DamageAmp> Amplifies = new();
}