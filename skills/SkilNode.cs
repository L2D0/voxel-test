using Godot;

[GlobalClass]
public partial class SkillNode : Resource
{
    [Export] public string Id = "";
    [Export] public string DisplayName = "Skill";
    [Export(PropertyHint.MultilineText)] public string Description = "";

    [ExportGroup("Placement")]
    [Export] public Element Element = Element.Fire;
    [Export] public Element FusionWith = Element.None;           // set for fusion nodes only
    [Export(PropertyHint.Range, "0,3")] public float Ring = 0.5f; // 1 = T1/T2 line, 2 = T2/T3 line, 3 = outer edge
    [Export(PropertyHint.Range, "-18,18")] public float AngleOffset = 0f;   // degrees from the wedge centre

    [ExportGroup("Unlocking")]
    [Export] public Godot.Collections.Array<SkillNode> Prerequisites = new();

    [ExportGroup("Effects")]
    [Export] public Godot.Collections.Array<StatModifier> Modifiers = new();
    [Export] public string[] Flags = System.Array.Empty<string>();

    public const int ElementCount = 10;
    public const float WedgeDegrees = 360f / ElementCount;

    public bool IsFusion => FusionWith != Element.None;

    // Wheel order matches the Element enum: Fire is wedge 0 (top right), Earth is wedge 9 (top left)
    public static int WheelIndex(Element e) => (int)e - 1;

    public float AngleDegrees()
    {
        int a = WheelIndex(Element);
        if (!IsFusion) return a * WedgeDegrees + WedgeDegrees / 2f + AngleOffset;

        // Fusions sit exactly on the border the two elements share
        int b = WheelIndex(FusionWith);
        if ((b - a + ElementCount) % ElementCount == 1) return b * WedgeDegrees;
        if ((a - b + ElementCount) % ElementCount == 1) return a * WedgeDegrees;

        GD.PushWarning($"Fusion '{DisplayName}': {Element} and {FusionWith} aren't neighbours");
        return a * WedgeDegrees;
    }
}