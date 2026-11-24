using Godot;

// What the player has in hand: the equipped focus's model. Every machine builds its own from the focus id
// that comes with the loadout. A model's origin is its grip, so it sits straight on the hand.
public partial class HeldItem : Node3D
{
    // Right side, at hip height (the body's centre; feet are at -1). Front is -Z.
    public static readonly Vector3 HandOffset = new(0.35f, 0f, -0.1f);

    private string _held = "";
    private Node3D _model;

    public void Hold(string itemId)
    {
        itemId ??= "";
        if (itemId == _held) return;
        _held = itemId;

        _model?.QueueFree();
        _model = null;
        if (ItemDatabase.Get(itemId)?.Model is PackedScene scene && scene.Instantiate() is Node3D model)
        {
            _model = model;
            AddChild(model);
        }
    }
}
