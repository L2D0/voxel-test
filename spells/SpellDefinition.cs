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

    public virtual void Begin(SpellContext ctx) { }
    public virtual void Update(SpellContext ctx, float dt) { }
    public virtual void Release(SpellContext ctx) { }
    public virtual void Cancel(SpellContext ctx) { }
}

public class SpellContext
{
    public PlayerController Caster;
    public Vector3 CastOrigin;
    public Vector3 AimPoint;
    public Vector3 AimNormal;
    public Vector3 AimDirection;
    public bool AimHit;
    public float HeldTime;
    public Node3D Instance;   // anything the spell spawns and needs to keep track of (preview, pillar)
}