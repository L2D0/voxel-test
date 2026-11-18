using Godot;

// What kind of thing a snap target is. Spells list the kinds they lock onto (SpellDefinition.SnapsTo).
[System.Flags]
public enum SnapKind { None = 0, Anchor = 1, Tomb = 2 }

// Anything the aim point can lock onto: Crystal anchors, tombs. Put its collider on the Structures layer (3).
public interface ISnapTarget
{
    Vector3 SnapPoint { get; }
    SnapKind Kind { get; }   // can be more than one: a Stone pillar under Geomancy could report Anchor too
}
