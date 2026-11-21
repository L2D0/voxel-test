using Godot;

// Anything the player can use with the interact key (E) by looking at it up close.
// Interact runs on the interacting player's own machine; anything shared goes through the host from there.
public interface IInteractable
{
    void Interact(PlayerController player);
}
