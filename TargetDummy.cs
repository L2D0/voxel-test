using Godot;

public partial class TargetDummy : StaticBody3D
{
    [Export] public float RespawnTime = 3f;

    private static readonly Color BaseColor = new Color(0.8f, 0.3f, 0.25f);

    private Health _health;
    private Label3D _label;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _label = GetNode<Label3D>("HealthLabel");

        _material = new StandardMaterial3D { AlbedoColor = BaseColor };
        GetNode<MeshInstance3D>("MeshInstance3D").MaterialOverride = _material;

        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
        UpdateLabel();
    }

    private void OnDamaged(float amount, Node source)
    {
        UpdateLabel();

        // Flash white, then fade back
        _material.AlbedoColor = Colors.White;
        CreateTween().TweenProperty(_material, "albedo_color", BaseColor, 0.2);
    }

    private void OnDied(Node source)
    {
        _label.Text = "DEAD";
        GetTree().CreateTimer(RespawnTime).Timeout += () =>
        {
            _health.Reset();
            UpdateLabel();
        };
    }

    private void UpdateLabel() => _label.Text = $"{_health.Current:0} / {_health.MaxHealth:0}";
}