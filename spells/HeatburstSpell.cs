using Godot;

[GlobalClass]
public partial class HeatburstSpell : SpellDefinition
{
    [Export] public PackedScene ProjectileScene;

    [ExportGroup("Projectile")]
    [Export] public float ProjectileSpeed = 30f;
    [Export] public float Lifetime = 1.5f;

    [ExportGroup("Explosion")]
    [Export] public float Radius = 4f;
    [Export] public float Damage = 30f;
    [Export] public float Knockback = 8f;
    [Export] public float SelfKnockback = 11f;
    [Export] public float SelfUpwardBias = 0.6f;
    [Export(PropertyHint.Range, "0,1")] public float SelfDamageMultiplier = 0.35f;
    [Export(PropertyHint.Range, "0,1")] public float MinFalloff = 0.3f;
    [ExportGroup("Lingering")]
    [Export] public GroundEffectDefinition LeavesEffect;   // optional: left behind where it explodes

    public override bool Begin(SpellContext ctx)
    {
        var projectile = ProjectileScene.Instantiate<HeatburstProjectile>();
        projectile.Ctx = ctx;
        projectile.Direction = ctx.AimDirection;

        ctx.Caster.GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = ctx.CastOrigin;
        return true;
    }
}