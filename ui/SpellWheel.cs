using Godot;
using System.Collections.Generic;

public partial class SpellWheel : Control
{
    [Export] public SpellCaster Caster;
    [Export] public float Radius = 170f;
    [Export] public float InnerRadius = 55f;
    [Export] public float Deadzone = 35f;
    [Export] public float CursorSensitivity = 1f;

    private static readonly Color SliceColor = new Color(0.15f, 0.15f, 0.18f, 0.75f);
    private static readonly Color ActiveColor = new Color(0.35f, 0.35f, 0.42f, 0.85f);
    private static readonly Color HoverColor = new Color(1f, 0.75f, 0.35f, 0.9f);
    private static readonly Color ChargeColor = new Color(1f, 0.85f, 0.5f);

    private Font _font;
    private Vector2 _cursor;
    private int _hovered = -1;
    private bool _open;

    public override void _Ready()
    {
        _font = GetThemeDefaultFont();
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private static bool MenuOpen => GameState.MenuOpen;

    public override void _Input(InputEvent e)
    {
        if (e.IsActionPressed("spell_wheel") && !MenuOpen)
        {
            Open();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_open) return;

        // Hovering a spell and pressing a number key binds it to that hotbar slot
        if (e is InputEventKey { Pressed: true, Echo: false } key && _hovered >= 0
            && Hotbar.SlotForKey(key.PhysicalKeycode) is int slot)
        {
            Hotbar.Of(this)?.BindSpell(slot, Caster.Spells[_hovered]);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionReleased("spell_wheel"))
        {
            Close(confirm: true);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion motion)
        {
            _cursor += motion.Relative * CursorSensitivity;
            if (_cursor.Length() > Radius) _cursor = _cursor.Normalized() * Radius;
            UpdateHover();
            GetViewport().SetInputAsHandled();   // the camera never sees this movement
        }
        else if (e is InputEventMouseButton)
        {
            GetViewport().SetInputAsHandled();   // no accidental casts while choosing
        }
    }

    public override void _Process(double delta)
    {
        if (!_open) return;
        if (MenuOpen) Close(confirm: false);   // pause menu opened on top, so cancel
        QueueRedraw();                          // charge arcs refill live
    }

    private void Open()
    {
        _open = true;
        _cursor = Vector2.Zero;
        _hovered = -1;
        Visible = true;
    }

    private void Close(bool confirm)
    {
        if (confirm && _hovered >= 0) Caster.SelectSpell(_hovered);
        _open = false;
        Visible = false;
    }

    private void UpdateHover()
    {
        int count = Caster.Spells.Count;
        if (count == 0 || _cursor.Length() < Deadzone)
        {
            _hovered = -1;
            return;
        }

        float slice = Mathf.Tau / count;
        float angle = Mathf.PosMod(Mathf.Atan2(_cursor.X, -_cursor.Y), Mathf.Tau);   // 0 at top, clockwise
        _hovered = (int)Mathf.PosMod(Mathf.Floor((angle + slice / 2f) / slice), count);
    }

    // Angle measured from the top, clockwise, as a screen direction
    private static Vector2 Dir(float angle) => new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));

    public override void _Draw()
    {
        int count = Caster.Spells.Count;
        if (count == 0) return;

        Vector2 center = Size / 2f;
        float slice = Mathf.Tau / count;
        const int arcSteps = 16;

        DrawCircle(center, Radius + 14f, new Color(0f, 0f, 0f, 0.3f));

        for (int i = 0; i < count; i++)
        {
            SpellDefinition spell = Caster.Spells[i];
            float a0 = i * slice - slice / 2f + 0.02f;   // small gap between slices
            float a1 = a0 + slice - 0.04f;

            // Ring segment: outer arc forward, inner arc back
            var points = new List<Vector2>();
            for (int s = 0; s <= arcSteps; s++) points.Add(center + Dir(Mathf.Lerp(a0, a1, s / (float)arcSteps)) * Radius);
            for (int s = arcSteps; s >= 0; s--) points.Add(center + Dir(Mathf.Lerp(a0, a1, s / (float)arcSteps)) * InnerRadius);

            Color color = i == _hovered ? HoverColor : i == Caster.ActiveIndex ? ActiveColor : SliceColor;
            DrawColoredPolygon(points.ToArray(), color);

            // Charge arc along the outer edge (DrawArc measures from +X, so shift by -90°)
            float fill = Caster.ChargesOf(i) / Caster.MaxChargesOf(i);
            if (fill > 0f)
                DrawArc(center, Radius + 7f, a0 - Mathf.Pi / 2f, Mathf.Lerp(a0, a1, fill) - Mathf.Pi / 2f, arcSteps, ChargeColor, 4f);

            // Name in the middle of the slice
            Vector2 labelPos = center + Dir(i * slice) * (InnerRadius + Radius) / 2f;
            Vector2 textSize = _font.GetStringSize(spell.SpellName, HorizontalAlignment.Left, -1, 16);
            DrawString(_font, labelPos + new Vector2(-textSize.X / 2f, 6f), spell.SpellName,
                HorizontalAlignment.Left, -1, 16, Colors.White);
        }

        // Virtual cursor
        DrawCircle(center + _cursor, 5f, Colors.White);
    }
}