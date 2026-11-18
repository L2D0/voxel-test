using Godot;

// Places a TestAnchor where you aim, so snapping spells (Test Tether) have something to lock onto.
[GlobalClass]
public partial class AnchorTestSpell : SpellDefinition
{
    [ExportGroup("Anchor")]
    [Export] public float Lifetime = 30f;

    public AnchorTestSpell()
    {
        SpellName = "Test Anchor";
        Element = Element.Crystal;
        Mode = CastMode.Instant;
        Range = 25f;
        MaxCharges = 3;
        RechargeTime = 2f;
    }

    public override bool IsTargetValid(SpellContext ctx) =>
        ctx.AimHit && ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= ctx.Stat(nameof(Range));

    public override bool Begin(SpellContext ctx)
    {
        var anchor = new TestAnchor { Lifetime = ctx.Stat(nameof(Lifetime)) };
        ctx.Caster.GetTree().CurrentScene.AddChild(anchor);
        anchor.GlobalPosition = ctx.AimPoint;
        return true;
    }
}
