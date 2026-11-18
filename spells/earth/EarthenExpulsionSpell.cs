using Godot;

// Hold to charge: a marker crawls along the ground toward wherever you're aiming.
// Release: a spike erupts under the marker and throws whatever's standing there into the air,
// harder the longer you charged.
[GlobalClass]
public partial class EarthenExpulsionSpell : SpellDefinition
{
    [ExportGroup("Expulsion")]
    [Export] public float Damage = 20f;
    [Export] public float MinLaunch = 6f;           // upward speed at no charge
    [Export] public float MaxLaunch = 16f;          // upward speed at full charge
    [Export] public float Radius = 1.6f;            // how wide the eruption catches
    [Export] public float MarkerSpeed = 20f;        // how fast the marker crawls toward your aim
    [Export] public float SpikeHeight = 2.6f;
    [Export] public float SpikeLifetime = 0.2f;
    [Export] public Color MarkerColor = new(0.85f, 0.7f, 0.45f, 0.6f);

    public EarthenExpulsionSpell()
    {
        SpellName = "Earthen Expulsion";
        Element = Element.Earth;
        Mode = CastMode.Hold;
        ChargeTime = 1f;
        Range = 20f;
        RechargeTime = 5f;
        Cost = 25f;
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

        // Starts at the caster's feet
        ctx.AimHit = GroundBelow(ctx.Caster, ctx.Caster.GlobalPosition, out ctx.AimPoint, out ctx.AimNormal);
        PlaceMarker(ctx);
        return true;
    }

    public override void Update(SpellContext ctx, float dt)
    {
        if (ctx.Instance == null) return;   // not the caster's machine

        // Crawl toward the crosshair, staying on the ground and within range.
        // ctx.AimPoint is what gets sent with the release, so it always holds the marker's spot.
        SpellContext aim = ctx.Caster.BuildSpellContext();
        if (aim.AimHit)
        {
            Vector3 goal = aim.AimPoint;
            Vector3 fromCaster = goal - ctx.Caster.GlobalPosition;
            float range = ctx.Stat(nameof(Range));
            if (fromCaster.Length() > range) goal = ctx.Caster.GlobalPosition + fromCaster.Normalized() * range;

            Vector3 next = ctx.AimPoint.MoveToward(goal, ctx.Stat(nameof(MarkerSpeed)) * dt);
            if (GroundBelow(ctx.Caster, next, out Vector3 point, out Vector3 normal))
            {
                ctx.AimPoint = point;
                ctx.AimNormal = normal;
                ctx.AimHit = true;
            }
        }
        PlaceMarker(ctx);
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
        var query = PhysicsRayQueryParameters3D.Create(near + Vector3.Up * 2f, near + Vector3.Down * 6f, SpellFx.WorldLayer);
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
            if (Teams.AreAllies(ctx.Caster, body)) continue;

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
