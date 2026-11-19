using Godot;

// Channel test: a beam that follows where you look while held, draining mana per second.
[GlobalClass]
public partial class BeamTestSpell : SpellDefinition
{
    [ExportGroup("Beam")]
    [Export] public float DamagePerSecond = 20f;
    [Export] public float TickInterval = 0.2f;
    [Export] public Color BeamColor = new(1f, 0.6f, 0.25f, 0.8f);

    public BeamTestSpell()
    {
        SpellName = "Test Beam";
        Element = Element.Fire;
        Mode = CastMode.Channel;
        Range = 25f;   // the base Range stat, so it gets the range bubble and range modifiers
        CostPerSecond = 15f;
    }

    public override bool Begin(SpellContext ctx)
    {
        var beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1f, RadialSegments = 8 },
            MaterialOverride = SpellFx.Glow(BeamColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(beam);
        ctx.Instance = beam;
        return true;
    }

    public override void Update(SpellContext ctx, float dt)
    {
        if (ctx.Instance == null) return;

        // Live aim, not the aim at the moment of casting: the camera's rotation is synced, so this works everywhere
        Vector3 from = ((ICaster)ctx.Caster).LiveCastOrigin;
        Vector3 to = from + ((ICaster)ctx.Caster).LiveAimDirection * ctx.Stat(nameof(Range));

        var query = PhysicsRayQueryParameters3D.Create(from, to, SpellFx.AnythingMask,
        new Godot.Collections.Array<Rid> { ((CollisionObject3D)ctx.Caster).GetRid() });
        var hit = ctx.Caster.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) to = (Vector3)hit["position"];

        SpellFx.StretchBetween(ctx.Instance, from, to);

        // Host only: damage whatever the beam touches, a tick at a time
        if (!ctx.Authoritative) return;
        ctx.TickTimer -= dt;
        if (ctx.TickTimer > 0f) return;

        float interval = ctx.Stat(nameof(TickInterval));
        ctx.TickTimer = interval;

        if (hit.Count == 0 || hit["collider"].As<Node>() is not Node body) return;
        if (!Teams.CanHarm(ctx.Caster, body)) return;

        Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(ctx, ctx.Stat(nameof(DamagePerSecond)) * interval));
        ctx.ApplyStatuses(body);
    }

    public override void Release(SpellContext ctx) => ctx.Instance?.QueueFree();
    public override void Cancel(SpellContext ctx) => ctx.Instance?.QueueFree();
}
