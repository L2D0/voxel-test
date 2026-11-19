using Godot;

public enum Response
{
    Ignore,   // pays it no attention
    Attack,   // notices it, then hunts it
    Flee,     // runs the moment it sees it
    Avoid,    // keeps its distance, but doesn't bolt
    Defend,   // leaves it alone until it, or the pack, gets hit
}

// One line of an enemy's response table: "when I see something tagged Tag, do Response".
// The first line whose tag matches wins, so put the specific ones first.
[GlobalClass]
public partial class TagResponse : Resource
{
    [Export] public string Tag = "Player";
    [Export] public Response Response = Response.Attack;
}
