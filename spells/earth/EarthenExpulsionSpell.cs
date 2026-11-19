using Godot;

// Hold to charge: a marker runs out along the ground from your feet, in whichever direction
// you're facing (only your horizontal aim counts). It never comes back toward you.
// Release, or let it reach full range: a spike erupts under the marker and throws whatever's
// standing there into the air, harder the longer you charged.
[GlobalClass]
public partial class EarthenExpulsionSpell : SpellDefinition
{
    [ExportGroup("Expulsion")]
    [Export] public float Damage = 20f;
    [Export] public float MinLaunch = 6f;           // upward speed at no charge
    [Export] public float MaxLaunch = 16f;          // upward speed at full charge
    [Export] public float Radius = 1.6f;            // how wide the eruption catches
    [Export] public float MarkerSpeed = 12f;        // how fast the marker runs out from you
    [Export] public float SpikeHeight = 2.6f;
    [Export] public float SpikeLifetime = 0.2f;
    [Export] public Color MarkerColor = new(0.85f, 0.7f, 0.45f, 0.6f);

    public EarthenExpulsionSpell()
    {
        SpellName = "Earthen Expulsion";
        Element = Element.Earth;
        Mode = CastMode.Hold;
        ChargeTime = 1f;
        Range = 18f;   // the marker fires on its own when it gets this far
        RechargeTime = 5f;
        Cost = 25f;
        FaceAimWhileHeld = true;
    }

    // ---------- Charging ----------

    // The marker only exists on the caster's own machine. Everyone else sees the eruption.
    public override bool Begin(SpellContext ctx)
    {
        if (!ctx.Caster.IsMultiplayerAuthority()) return true;

        var marker = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.8f },
            MaterialOverride = SpellFx.Glow(MarkerColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(marker);
        ctx.Instance = marker;

        // Starts at the caster's feet. TickTimer holds how far it has travelled.
        ctx.TickTimer = 0f;
        ctx.AimHit = GroundBelow(ctx.Caster, ctx.Caster.GlobalPosition, out ctx.AimPoint, out ctx.AimNormal);
        PlaceMarker(ctx);
        return true;
    }

    public override void Update(SpellContext ctx, float dt)
    {
        if (ctx.Instance == null) return;   // not the caster's machine

        // Distance only ever grows; direction is the camera's facing, flattened, so looking up or down changes nothing
        float range = ctx.Stat(nameof(Range));
        ctx.TickTimer = Mathf.Min(ctx.TickTimer + ctx.Stat(nameof(MarkerSpeed)) * dt, range);

        Vector3 facing = ((ICaster)ctx.Caster).LiveAimDirection;
        facing = new Vector3(facing.X, 0f, facing.Z);
        if (facing.LengthSquared() < 0.0001f) facing = Vector3.Forward;
        Vector3 spot = ctx.Caster.GlobalPosition + facing.Normalized() * ctx.TickTimer;

        // ctx.AimPoint is what gets sent with the release, so it always holds the marker's spot.
        // Over a drop or a wall with no ground under it, the marker hides and a release there does nothing.
        ctx.AimHit = GroundBelow(ctx.Caster, spot, out ctx.AimPoint, out ctx.AimNormal);
        PlaceMarker(ctx);

        // Full range: fire on its own, as if released
        if (ctx.TickTimer >= range) ctx.Casting.ForceRelease();
    }

    private static void PlaceMarker(SpellContext ctx)
    {
        var marker = (MeshInstance3D)ctx.Instance;
        marker.Visible = ctx.AimHit;
        marker.GlobalTransform = new Transform3D(StonePillarSpell.BasisFromUp(ctx.AimNormal), ctx.AimPoint + ctx.AimNormal * 0.05f);

        float size = 0.6f + ctx.ChargeFraction * 0.6f;   // swells as it charges
        marker.Scale = new Vector3(size, 1f, size);
    }

    private static bool GroundBelow(Node3D from, Vector3 near, out Vector3 point, out Vector3 normal)
    {
        var query = PhysicsRayQueryParameters3D.Create(near + Vector3.Up * 4f, near + Vector3.Down * 8f, SpellFx.WorldLayer);
        var hit = from.GetWorld3D().DirectSpaceState.IntersectRay(query);
        point = hit.Count > 0 ? (Vector3)hit["position"] : near;
        normal = hit.Count > 0 ? (Vector3)hit["normal"] : Vector3.Up;
        return hit.Count > 0;
    }

    // ---------- Eruption ----------

    // Runs on every machine. AimPoint, AimNormal and HeldTime here are the caster's,
    // sent with the release, so every machine erupts in the same spot with the same power.
    public override void Release(SpellContext ctx)
    {
        ctx.Instance?.QueueFree();
        if (!ctx.AimHit) return;

        float charge = ctx.ChargeFraction;
        float height = ctx.Stat(nameof(SpikeHeight)) * (0.6f + 0.4f * charge);

        var spike = new ExpulsionSpike
        {
            Ctx = ctx,
            Name = $"Expulsion_{ctx.Caster.Name}_{ctx.CastId}",
            Axis = ctx.AimNormal,
            Radius = ctx.Stat(nameof(Radius)) * 0.45f,
            MaxHeight = height,
            MinHeight = height,
            RiseSpeed = 35f,
            Windup = 0f,
            Lifetime = ctx.Stat(nameof(SpikeLifetime)),
            WarningTime = 0f,
            SegmentHeight = height,
            Transform = new Transform3D(StonePillarSpell.BasisFromUp(ctx.AimNormal), ctx.AimPoint - ctx.AimNormal * 0.3f),
        };
        ctx.Caster.GetTree().CurrentScene.AddChild(spike);

        if (!ctx.Authoritative) return;   // the host decides who gets hit and thrown

        float damage = ctx.Stat(nameof(Damage)) * (0.5f + 0.5f * charge);
        float launch = Mathf.Lerp(ctx.Stat(nameof(MinLaunch)), ctx.Stat(nameof(MaxLaunch)), charge);

        foreach (Node3D body in SpellFx.BodiesInSphere(ctx.Caster, ctx.AimPoint, ctx.Stat(nameof(Radius)), SpellFx.CharacterLayer))
        {
            if (!Teams.CanHarm(ctx.Caster, body)) continue;

            Health.Of(body)?.TakeDamage(DamageInfo.FromSpell(ctx, damage));
            ctx.ApplyStatuses(body);

            // Mostly up, with a little shove away from the centre so a cluster scatters
            if (body is IImpulseReceiver receiver)
            {
                Vector3 outward = body.GlobalPosition - ctx.AimPoint;
                outward = (outward - ctx.AimNormal * outward.Dot(ctx.AimNormal)).Normalized();
                receiver.AddImpulse(ctx.AimNormal * launch + outward * launch * 0.15f);
            }
        }
    }

    public override void Cancel(SpellContext ctx) => ctx.Instance?.QueueFree();
}