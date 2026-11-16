using Godot;
using System.Collections.Generic;

public partial class SkillTree : Node
{
    [Signal] public delegate void ChangedEventHandler();

    [Export] public Godot.Collections.Array<SkillNode> Nodes = new();

    private ModifierStack _mods;
    private readonly HashSet<SkillNode> _unlocked = new();
    private readonly Dictionary<string, int> _flags = new();

    public override void _Ready()
    {
        _mods = GetParent().GetNode<ModifierStack>("Modifiers");
    }

    public bool IsUnlocked(SkillNode node) => _unlocked.Contains(node);

    public bool CanUnlock(SkillNode node)
    {
        if (IsUnlocked(node)) return false;
        foreach (SkillNode prereq in node.Prerequisites)
            if (!IsUnlocked(prereq)) return false;
        return true;
    }

    public bool HasFlag(string flag) => _flags.TryGetValue(flag, out int count) && count > 0;

    public void Toggle(SkillNode node)
    {
        if (IsUnlocked(node)) Lock(node);
        else Unlock(node);
    }

    public bool Unlock(SkillNode node)
    {
        if (!CanUnlock(node)) return false;

        _unlocked.Add(node);
        foreach (StatModifier mod in node.Modifiers) _mods.Add(mod, node);
        foreach (string flag in node.Flags) _flags[flag] = _flags.GetValueOrDefault(flag) + 1;

        EmitSignal(SignalName.Changed);
        return true;
    }

    public void Lock(SkillNode node)
    {
        if (!IsUnlocked(node)) return;

        // Cascade: anything that depends on this node is unlearned first
        foreach (SkillNode other in Nodes)
            if (IsUnlocked(other) && other.Prerequisites.Contains(node))
                Lock(other);

        _unlocked.Remove(node);
        _mods.RemoveSource(node);
        foreach (string flag in node.Flags) _flags[flag] = Mathf.Max(0, _flags.GetValueOrDefault(flag) - 1);

        EmitSignal(SignalName.Changed);
    }
}