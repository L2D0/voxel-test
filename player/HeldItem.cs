using Godot;

// What the player has in hand: the equipped focus's model. Every machine builds its own from the focus id
// that comes with the loadout. A model's origin is its grip.
// If the player's body model has a hand node (HandNode), the item rides on it so it follows the arm;
// otherwise it sits at HandOffset on the placeholder bean.
public partial class HeldItem : Node3D
{
    public const string HandNode = "hand_R";

    // Bean fallback: right side, at hip height (the body's centre; feet are at -1). Front is -Z.
    public static readonly Vector3 HandOffset = new(0.35f, 0f, -0.1f);

    // From the hand node (the wrist) to the middle of the fist, in the hand's own space.
    // Manny at Blockbench's export scale: about 1 px in and 4 px down from the wrist.
    public static readonly Vector3 GripOffset = new(-0.075f, -0.27f, 0f);

    private string _held = "";
    private Node3D _model;
    private bool _onHand;

    public void Hold(string itemId)
    {
        FindHand();

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

    // Moves onto the body model's hand, once, if there is one. owned: false because the hand
    // belongs to the imported model's scene, not the player's.
    private void FindHand()
    {
        if (_onHand || GetParent() is not Node body) return;
        if (body.FindChild(HandNode, true, false) is not Node3D hand) return;

        Reparent(hand, false);
        Position = GripOffset;
        Rotation = Vector3.Zero;
        Scale = Vector3.One;
        _onHand = true;
    }
}
