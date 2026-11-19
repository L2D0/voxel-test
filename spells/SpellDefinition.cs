using Godot;

// Instant:    resolves the moment you press cast.
// Hold:       runs while held, resolves on release; ChargeTime gives it a charge-up (ctx.ChargeFraction).
// Channel:    runs while held, drains CostPerSecond, ends on release, empty mana or MaxChannelTime.
// Duration:   runs for Duration seconds; RechargeAfterEnd and AllowRecast are mostly for these.
// Delayed:    placed now, resolves after Delay seconds (Expire is the moment it lands).
// MultiPoint: pick PointCount spots with separate clicks while aiming, then it resolves like Instant.
//             Letting go of aim throws away the picks so far.
public enum CastMode { Instant, Hold, Channel, Duration, Delayed, MultiPoint }
public enum CostType { Mana, Health }

// What right click does. Anything but None replaces aiming (no zoom) for that spell.
// SelfCast:    cast it on yourself.
// AltCast:     cast it with ctx.IsAlt = true, for spells with a second behaviour (Ivy whip's latch).
public enum AltMode { None, SelfCast, AltCast }

[System.Flags]
public enum TargetFilter { None = 0, Enemies = 1, Allies = 2 }
public enum Element { None, Fire, Toxic, Dark, Shock, Wind, Water, Nature, Life, Crystal, Earth }

public partial class SpellDefinition : Resource
{
    [Export] public string SpellName = "Spell";
    [Export] public Element Element = Element.None;
    [Export] public Texture2D Icon;
    [Export] public CastMode Mode = CastMode.Instant;
    [Export] public int MaxCharges = 1;
    [Export] public float RechargeTime = 1f;
    [Export] public float Range = 0f;   // how far it reaches; 0 = unlimited, and no range bubble
    [Export] public int MaxActive = 0;  // how many of its objects can exist at once; the oldest goes first. 0 = no cap
    [Export] public Godot.Collections.Array<StatusDefinition> AppliesStatuses = new();
    [Export] public CostType PaysWith = CostType.Mana;
    [Export] public float Cost = 0f;
    [Export] public float NoiseRadius = 20f;   // how far enemies hear the cast; 0 = silent

    [ExportGroup("Timing")]
    [Export] public float ChargeTime = 0f;          // Hold: seconds to full power (0 = no charge-up)
    [Export] public float CostPerSecond = 0f;       // Channel: drained while active
    [Export] public float MaxChannelTime = 0f;      // Channel: 0 = until released or out of mana
    [Export] public float Duration = 0f;            // Duration: how long it lasts
    [Export] public float Delay = 0f;               // Delayed: time before it lands
    [Export] public int PointCount = 2;             // MultiPoint: how many spots to pick
    [Export] public bool RechargeAfterEnd = false;  // charges don't refill while a cast of this spell is running
    [Export] public bool AllowRecast = false;       // pressing cast while it's running calls Recast, then ends it
    [Export] public bool FaceAimWhileHeld = false;  // Hold and Channel: the player turns with the camera until released

    [ExportGroup("Targeting")]
    [Export] public bool RequiresTarget = false;    // can't be cast without a valid entity under the crosshair
    [Export] public TargetFilter Targets = TargetFilter.Enemies;
    [Export] public float AssistRadius = 0.4f;      // how far off the crosshair a target can be and still count
    [Export] public AltMode AltMode = AltMode.None;
    [Export] public SnapKind SnapsTo = SnapKind.None;   // the aim point jumps onto snap targets of these kinds
    [Export] public bool RequiresSnap = false;          // only castable on one of them (Crystal binds)

    public virtual bool IsTargetValid(SpellContext ctx) => true;

    // ---------- Lifecycle (runs on every machine) ----------

    // Return false if the cast couldn't start; nothing is tracked
    public virtual bool Begin(SpellContext ctx) => true;
    public virtual void Update(SpellContext ctx, float dt) { }
    public virtual void Release(SpellContext ctx) { }   // Hold, Channel: the player let go (or ran dry)
    public virtual void Expire(SpellContext ctx) { }    // Duration, Delayed: time ran out, or ended early
    public virtual void Cancel(SpellContext ctx) { }    // interrupted before finishing (switched spell)
    public virtual void Recast(SpellContext ctx) { }    // AllowRecast: runs just before Expire
                                                        // Chains: the host works out the route and every machine draws it from the same list of hops
    public virtual void ShowChain(SpellContext ctx, Node3D[] hops) { }
    // Optional targeting preview, shown while aiming with this spell active
    public virtual Node3D CreatePreview() => null;
    public virtual void UpdatePreview(Node3D preview, SpellContext ctx, bool valid) { }
}

public class SpellContext
{
    public Node3D Caster;                                           // a player or an enemy
    public PlayerController Player => Caster as PlayerController;   // null when an enemy cast it
    public SpellDefinition Spell;
    public ModifierStack Mods;
    public int SpellIndex;

    public Vector3 CastOrigin;
    public Vector3 AimPoint;
    public Vector3 AimNormal;
    public Vector3 AimDirection;
    public GodotObject AimCollider;
    public bool AimHit;
    public Vector3[] Points = System.Array.Empty<Vector3>();   // MultiPoint picks, in order
    public Node3D[] PointTargets = System.Array.Empty<Node3D>();   // what each pick snapped to (null if nothing)
    public Node3D Target;         // targeted spells: who it was cast on (the caster, for a self-cast)
    public bool IsAlt;            // cast with right click
    public bool Snapped;          // the aim point was moved onto an ISnapTarget

    public float HeldTime;        // seconds since Begin
    public float Remaining;       // Duration, Delayed: seconds left (spells may extend it)
    public float TickTimer;       // free for spells that tick
    public Node3D Instance;       // whatever the spell spawned
    public int CastId;            // unique per caster, the same on every machine
    public bool Authoritative;    // true on the host: only the host applies damage and shared effects

    // The effective value of a stat for this cast: base value plus every active modifier
    public float Stat(string name) => Mods != null ? Mods.Get(Spell, name) : Spell.Get(name).AsSingle();
    public bool HasFlag(string flag) => Player?.Skills != null && Player.Skills.HasFlag(flag);

    // Hold spells: 0 to 1, how far through the charge-up
    public float ChargeFraction
    {
        get
        {
            float time = Stat(nameof(SpellDefinition.ChargeTime));
            return time <= 0f ? 1f : Mathf.Clamp(HeldTime / time, 0f, 1f);
        }
    }

    // Registers something this cast spawned, so the spell's MaxActive cap can remove the oldest
    public void Track(Node node) => Casting?.Track(SpellIndex, node);

    public SpellCaster Casting => Caster.GetNodeOrNull<SpellCaster>("Spellcaster");   // null for enemies

    // Applies every status this spell carries to a target, if it can hold statuses
    public void ApplyStatuses(Node target)
    {
        var statuses = StatusEffects.Of(target);
        if (statuses == null) return;
        foreach (StatusDefinition status in Spell.AppliesStatuses)
            if (status != null) statuses.Apply(status, Caster);
    }
}
