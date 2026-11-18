using Godot;

// Dark Souls 2 style layout in a beige minimalist palette. Fades in when needed, out when not.
// Top left: the active spell's cooldown ring, with the health and mana bars running off to its right.
public partial class HUD : Control
{
    [ExportGroup("Fade")]
    [Export] public float HoldTime = 5f;              // stays this long after the last trigger
    [Export] public float FadeInSpeed = 6f;
    [Export] public float FadeOutSpeed = 1.5f;
    [Export] public float LowHealthFraction = 0.3f;   // always visible at or below this

    [ExportGroup("Layout")]
    [Export] public Vector2 Margin = new(32, 28);
    [Export] public float CooldownRadius = 24f;
    [Export] public float RingToBars = 12f;           // gap between the ring and the bars

    [ExportGroup("Bars")]
    [Export] public float PixelsPerPoint = 2.4f;      // bars grow with max HP and mana
    [Export] public float MaxBarLength = 640f;
    [Export] public float HealthHeight = 9f;
    [Export] public float ManaHeight = 5f;
    [Export] public float ShieldHeight = 4f;
    [Export] public float BarGap = 6f;
    [Export] public float TrailDelay = 0.5f;          // lost HP lingers this long before draining
    [Export] public float TrailSpeed = 80f;           // HP per second

    [ExportGroup("Colours")]
    [Export] public Color Frame = new(0.16f, 0.15f, 0.13f, 0.85f);
    [Export] public Color Back = new(0.09f, 0.085f, 0.08f, 0.5f);
    [Export] public Color HealthFill = new(0.87f, 0.83f, 0.72f);
    [Export] public Color TrailFill = new(0.72f, 0.42f, 0.33f);
    [Export] public Color ShieldFill = new(0.62f, 0.82f, 0.96f);
    [Export] public Color ManaFill = new(0.63f, 0.69f, 0.71f);
    [Export] public Color Ink = new(0.87f, 0.83f, 0.72f);   // crosshair, text, cooldown ring

    private PlayerController _player;
    private Health _health;
    private Mana _mana;
    private SpellCaster _caster;

    private float _wake, _alpha;
    private float _aimWake, _crosshairAlpha;
    private float _trail = -1f, _trailDelay;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _player = GetParent().GetParent<PlayerController>();   // Player > CanvasLayer > HUD
        _health = Health.Of(_player);
        _mana = _player.GetNodeOrNull<Mana>("Mana");
        _caster = _player.GetNodeOrNull<SpellCaster>("Spellcaster");

        _health.Damaged += (amount, source) =>
        {
            Wake();
            _trailDelay = TrailDelay;
        };
        if (_caster != null) _caster.Casted += OnCasted;
    }

    public override void _ExitTree()
    {
        if (_caster != null) _caster.Casted -= OnCasted;
    }

    private void Wake() => _wake = HoldTime;

    private void OnCasted()
    {
        Wake();
        _aimWake = 0.6f;   // keep the crosshair up briefly after a cast
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool aiming = _player.IsAiming;
        bool holding = _caster != null && _caster.IsHolding;
        bool targeting = _caster != null && _caster.AimReplaced;
        bool lowHealth = _health.Current <= _health.MaxHealth * LowHealthFraction;

        if (aiming || holding || lowHealth || GameState.MenuOpen) _wake = HoldTime;
        else _wake -= dt;
        _alpha = Fade(_alpha, _wake > 0f, dt);

        if (aiming || holding || targeting) _aimWake = 0.6f;
        else _aimWake -= dt;
        _crosshairAlpha = Fade(_crosshairAlpha, _aimWake > 0f, dt);

        // Damage trail: snaps up on healing, waits, then drains down to current HP
        if (_trail < _health.Current) _trail = _health.Current;
        else if (_trailDelay > 0f) _trailDelay -= dt;
        else _trail = Mathf.MoveToward(_trail, _health.Current, TrailSpeed * dt);

        QueueRedraw();
    }

    private float Fade(float value, bool on, float dt) =>
        Mathf.MoveToward(value, on ? 1f : 0f, (on ? FadeInSpeed : FadeOutSpeed) * dt);

    // ---------- Drawing ----------

    public override void _Draw()
    {
        if (_alpha > 0.001f)
        {
            float r = CooldownRadius;
            var ringCenter = new Vector2(Margin.X + r, Margin.Y + r);

            DrawCooldown(ringCenter);
            DrawBars(new Vector2(ringCenter.X + r + RingToBars, ringCenter.Y));
        }
        if (_crosshairAlpha > 0.001f) DrawCrosshair();
        if (_crosshairAlpha > 0.001f) DrawTargetMarker();
    }

    // left = where the bars start; the bar stack is centred vertically on left.Y
    private void DrawBars(Vector2 left)
    {
        float stack = HealthHeight + (_mana != null ? BarGap + ManaHeight : 0f);
        float y = left.Y - stack / 2f;

        var hp = new Rect2(left.X, y, BarLength(_health.MaxHealth), HealthHeight);
        DrawBarBack(hp);
        DrawFill(hp, _trail / _health.MaxHealth, TrailFill);
        DrawFill(hp, _health.Current / _health.MaxHealth, HealthFill);

        if (_health.Shield > 0f)   // a strip just above the health bar
        {
            var shield = new Rect2(left.X, y - ShieldHeight - 3f, BarLength(_health.Shield), ShieldHeight);
            DrawRect(shield.Grow(1f), Faded(Frame));
            DrawRect(shield, Faded(ShieldFill));
        }

        float bottom = y + HealthHeight;
        if (_mana != null)
        {
            y += HealthHeight + BarGap;
            var mana = new Rect2(left.X, y, BarLength(_mana.MaxMana), ManaHeight);
            DrawBarBack(mana);
            DrawFill(mana, _mana.Current / _mana.MaxMana, ManaFill);
            bottom = y + ManaHeight;
        }

        // Active spell's name, tucked under the bars
        SpellDefinition spell = _caster?.ActiveSpell;
        if (spell != null)
            DrawString(ThemeDB.FallbackFont, new Vector2(left.X, bottom + 16f), spell.SpellName,
                HorizontalAlignment.Left, -1, 13, Faded(Ink));
    }

    private void DrawCooldown(Vector2 center)
    {
        SpellDefinition spell = _caster?.ActiveSpell;
        if (spell == null) return;

        int index = _caster.ActiveIndex;
        float charges = _caster.ChargesOf(index);
        int max = _caster.MaxChargesOf(index);
        float r = CooldownRadius;

        DrawCircle(center, r + 2f, Faded(Frame));
        DrawCircle(center, r, Faded(Back));

        // Ring: full when every charge is ready, otherwise the next charge's progress
        float ringRadius = r - 4f;
        float progress = charges >= max ? 1f : charges - Mathf.Floor(charges);
        DrawArc(center, ringRadius, 0f, Mathf.Tau, 48, Faded(new Color(Ink, 0.15f)), 3f, true);
        if (progress > 0f)
            DrawArc(center, ringRadius, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * progress, 48, Faded(Ink), 3f, true);

        if (max > 1)   // charge count in the middle
        {
            string count = Mathf.FloorToInt(charges).ToString();
            DrawString(ThemeDB.FallbackFont, center + new Vector2(-r, 6f), count,
                HorizontalAlignment.Center, r * 2f, 18, Faded(Ink));
        }
        else if (charges >= 1f)   // single-charge spell: a dot means ready
        {
            DrawCircle(center, 3f, Faded(Ink));
        }
    }

    private void DrawCrosshair()
    {
        Vector2 c = Size / 2f;
        var ink = new Color(Ink, _crosshairAlpha);
        var shadow = new Color(0f, 0f, 0f, 0.5f * _crosshairAlpha);
        const float gap = 5f, length = 6f;

        foreach (Vector2 d in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
        {
            DrawLine(c + d * gap + Vector2.One, c + d * (gap + length) + Vector2.One, shadow, 2f);
            DrawLine(c + d * gap, c + d * (gap + length), ink, 2f);
        }
        DrawCircle(c, 1.5f, ink);
    }

    // Corner brackets around whatever a targeted spell is locked onto
    private void DrawTargetMarker()
    {
        Node3D target = _caster?.CurrentTarget;
        Camera3D camera = GetViewport().GetCamera3D();
        if (target == null || !IsInstanceValid(target) || camera == null) return;
        if (camera.IsPositionBehind(target.GlobalPosition)) return;

        Vector2 p = camera.UnprojectPosition(target.GlobalPosition);
        var ink = new Color(Ink, _crosshairAlpha);
        const float size = 16f, arm = 6f;

        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            Vector2 c = p + corner * size;
            DrawLine(c, c - new Vector2(corner.X * arm, 0f), ink, 2f);
            DrawLine(c, c - new Vector2(0f, corner.Y * arm), ink, 2f);
        }
    }

    private float BarLength(float points) => Mathf.Min(points * PixelsPerPoint, MaxBarLength);

    private void DrawBarBack(Rect2 rect)
    {
        DrawRect(rect.Grow(1.5f), Faded(Frame));
        DrawRect(rect, Faded(Back));
    }

    private void DrawFill(Rect2 rect, float fraction, Color color)
    {
        fraction = Mathf.Clamp(fraction, 0f, 1f);
        if (fraction <= 0f) return;
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X * fraction, rect.Size.Y)), Faded(color));
    }

    private Color Faded(Color c) => new(c.R, c.G, c.B, c.A * _alpha);
}
