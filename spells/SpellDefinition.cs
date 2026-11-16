using Godot;

public enum CastMode { Instant, Hold }
public enum Element { None, Fire, Toxic, Dark, Shock, Wind, Water, Nature, Life, Crystal, Earth }

public partial class SpellDefinition : Resource
{
    [Export] public string SpellName = "Spell";
    [Export] public Element Element = Element.None;
    [Export] public Texture2D Icon;
    [Export] public CastMode Mode = CastMode.Instant;
    [Export] public int MaxCharges = 1;
    [Export] public float RechargeTime = 1f;
    [Export] public float Range = 0f;   // 0 = unlimited, and no range bubble

    public virtual bool IsTargetValid(SpellContext ctx) => true;

    // Return false if the cast couldn't start (bad target); no charge is spent
    public virtual bool Begin(SpellContext ctx) => true;
    public virtual void Update(SpellContext ctx, float dt) { }
    public virtual void Release(SpellContext ctx) { }
    public virtual void Cancel(SpellContext ctx) { }

    // Optional targeting preview, shown while aiming with this spell active
    public virtual Node3D CreatePreview() => null;
    public virtual void UpdatePreview(Node3D preview, SpellContext ctx, bool valid) { }
}

public class SpellContext
{
    public PlayerController Caster;
    public SpellDefinition Spell;
    public ModifierStack Mods;

    public Vector3 CastOrigin;
    public Vector3 AimPoint;
    public Vector3 AimNormal;
    public Vector3 AimDirection;
    public GodotObject AimCollider;
    public bool AimHit;
    public float HeldTime;
    public Node3D Instance;

    // The effective value of a stat for this cast: base value plus every active modifier
    public float Stat(string name) => Mods != null ? Mods.Get(Spell, name) : Spell.Get(name).AsSingle();
    public bool HasFlag(string flag) => Caster?.Skills != null && Caster.Skills.HasFlag(flag);
}