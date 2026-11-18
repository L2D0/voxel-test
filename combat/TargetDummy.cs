using Godot;

public partial class TargetDummy : StaticBody3D
{
    [Export] public float RespawnTime = 3f;

    private static readonly Color BaseColor = new Color(0.8f, 0.3f, 0.25f);

    private Health _health;
    private StatusEffects _statuses;
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
        _health.Changed += (current, max) => UpdateLabel();
        _statuses = StatusEffects.Of(this);
        if (_statuses != null) _statuses.Changed += UpdateLabel;
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
        if (!Multiplayer.IsServer()) return;   // the host revives it; everyone hears about it through Changed
        GetTree().CreateTimer(RespawnTime).Timeout += () => _health.Reset();
    }

    private void UpdateLabel()
    {
        string text = $"{_health.Current:0} / {_health.Max:0}";
        if (_statuses != null)
            foreach (StatusEffects.Instance s in _statuses.Active)
                text += $"\n{s.Def.DisplayName} x{s.Stacks}";
        _label.Text = text;
    }
}