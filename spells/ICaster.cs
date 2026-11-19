using Godot;

// Anything that can cast spells: players, and enemies using abilities.
// Spells that track the caster's aim while running (beams, Expulsion's marker) read it through this.
public interface ICaster
{
    Vector3 LiveCastOrigin { get; }
    Vector3 LiveAimDirection { get; }
}
