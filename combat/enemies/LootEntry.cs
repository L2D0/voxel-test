using Godot;

// One possible drop. Stub for now: ItemId points at nothing until items exist (pipeline 6.9).
[GlobalClass]
public partial class LootEntry : Resource
{
    [Export] public string ItemId = "";
    [Export(PropertyHint.Range, "0,1")] public float Chance = 1f;
    [Export] public int MinCount = 1;
    [Export] public int MaxCount = 1;
}
