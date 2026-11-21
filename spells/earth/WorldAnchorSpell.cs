using Godot;

// Earth: hold to raise a waystone where you aim. You're rooted while it rises; let go before it's fully
// up and it sinks back down, costing nothing. Waystones are your personal travel network (pipeline 5.8).
//
// While charging, every machine shows a stand-in stone rising out of the ground. At full charge the host
// places the real structure in the same spot, and the stand-in goes.
[GlobalClass]
public partial class WorldAnchorSpell : SpellDefinition
{
    [ExportGroup("Waystone")]
    [Export] public PackedScene Structure;                                   // structures/waystone.tscn
    [Export(PropertyHint.Range, "0,1")] public float MinFloorNormal = 0.7f;  // only on fairly level ground
    [Export] public float RiseDepth = 2.4f;                                  // how far below ground the stone starts
    [Export] public float ShakeAmount = 0.03f;                               // jitter while it grinds up

    // Checked before the cast starts: aimed at level ground, in range
    public override bool IsTargetValid(SpellContext ctx)
    {
        if (!ctx.AimHit || ctx.AimNormal.Y < MinFloorNormal) return false;
        float range = ctx.Stat(nameof(Range));
        return range <= 0f || ctx.Caster.GlobalPosition.DistanceTo(ctx.AimPoint) <= range;
    }

    // Every machine: the stand-in starts buried
    public override bool Begin(SpellContext ctx)
    {
        if (Structure == null) return false;

        var stone = Structure.Instantiate<Structure>();
        stone.CollisionLayer = 0;                         // just a picture: nothing hits it, nothing saves it
        stone.ProcessMode = Node.ProcessModeEnum.Disabled;
        stone.GetNodeOrNull<Node3D>("Label")?.Hide();
        stone.Rotation = new Vector3(0f, YawFacing(ctx), 0f);
        ctx.Caster.GetTree().CurrentScene.AddChild(stone);
        stone.GlobalPosition = ctx.AimPoint + Vector3.Down * RiseDepth;

        ctx.Instance = stone;
        return true;
    }

    // Every machine: rise with the charge. The caster's machine lets go for you when it's fully up.
    public override void Update(SpellContext ctx, float dt)
    {
        if (ctx.Instance is not Node3D stone || !GodotObject.IsInstanceValid(stone)) return;

        float charge = ctx.ChargeFraction;
        Vector3 shake = new(GD.Randf() - 0.5f, 0f, GD.Randf() - 0.5f);
        stone.GlobalPosition = ctx.AimPoint + Vector3.Down * (RiseDepth * (1f - charge)) + shake * ShakeAmount;

        if (charge >= 1f && ctx.Caster.IsMultiplayerAuthority()) ctx.Casting?.ForceRelease();
    }

    // Every machine. RequiresFullCharge means this only runs once it's fully up; letting go early is a Cancel.
    public override void Release(SpellContext ctx)
    {
        if (ctx.Authoritative)
            StructureManager.Of(ctx.Caster)?.Place(Structure, ctx.AimPoint, YawFacing(ctx), Session.KeyOf(ctx.Caster));

        // Give the real one a moment to arrive on other machines before the stand-in goes
        if (ctx.Instance is Node3D stone)
            stone.GetTree().CreateTimer(0.3f).Timeout += () => { if (GodotObject.IsInstanceValid(stone)) stone.QueueFree(); };
    }

    // Let go early, stunned, or knocked off the ground: it sinks back
    public override void Cancel(SpellContext ctx)
    {
        if (ctx.Instance is not Node3D stone || !GodotObject.IsInstanceValid(stone)) return;
        Tween sink = stone.GetTree().CreateTween();   // the stone doesn't process, so its own tweens never run
        sink.TweenProperty(stone, "global_position", ctx.AimPoint + Vector3.Down * RiseDepth, 0.4);
        sink.TweenCallback(Callable.From(stone.QueueFree));
    }

    // Faces the caster, so you watch its front come up
    private static float YawFacing(SpellContext ctx)
    {
        Vector3 toCaster = ctx.Caster.GlobalPosition - ctx.AimPoint;
        return Mathf.Atan2(-toCaster.X, -toCaster.Z);
    }
}
