using Godot;

// Anything the aim point can lock onto: Crystal anchors, tombs. Put its collider on the Structures layer (3).
public interface ISnapTarget
{
    Vector3 SnapPoint { get; }
}
