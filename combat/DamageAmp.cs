using Godot;

// "While this status is on a target, incoming damage of this type is increased"
[GlobalClass]
public partial class DamageAmp : Resource
{
    [Export] public DamageType Type = DamageType.Physical;
    [Export] public float PerStack = 0.25f;   // 0.25 = +25% per stack
}