using Godot;
using System.Collections.Generic;

public partial class SkillTreeView : Control
{
    private static readonly string[] ElementNames =
        { "Fire", "Toxic", "Dark", "Shock", "Wind", "Water", "Nature", "Life", "Crystal", "Earth" };

    private static readonly Color[] ElementColors =
    {
        new Color(0.75f, 0.15f, 0.1f), new Color(0.25f, 0.68f, 0.16f), new Color(0.37f, 0.18f, 0.54f),
        new Color(0.91f, 0.73f, 0.05f), new Color(0.62f, 0.77f, 0.74f), new Color(0.31f, 0.66f, 0.84f),
        new Color(0.37f, 0.56f, 0.24f), new Color(0.89f, 0.83f, 0.63f), new Color(0.83f, 0.5f, 0.77f),
        new Color(0.55f, 0.31f, 0.15f),
    };

    private static readonly Color FusionRing = new Color(1f, 0.85f, 0.4f);

    private SkillTree _tree;
    private SkillNode _hovered;
    private Font _font;

    public override void _Ready()
    {
        _font = GetThemeDefaultFont();
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetTree(SkillTree tree)
    {
        _tree = tree;
        _tree.Changed += QueueRedraw;
    }

    private Vector2 Center => Size / 2f;
    private float WheelRadius => Mathf.Min(Size.X, Size.Y) * 0.38f;

    private static Vector2 Dir(float degrees)
    {
        float r = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
    }

    private Vector2 NodePos(SkillNode n) => Center + Dir(n.AngleDegrees()) * (n.Ring / 3f * WheelRadius);

    private SkillNode NodeAt(Vector2 pos)
    {
        foreach (SkillNode n in _tree.Nodes)
            if (pos.DistanceTo(NodePos(n)) < 14f) return n;
        return null;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_tree == null) return;

        if (e is InputEventMouseMotion motion)
        {
            SkillNode hovered = NodeAt(motion.Position);
            if (hovered != _hovered)
            {
                _hovered = hovered;
                QueueRedraw();
            }
        }
        else if (e is InputEventMouseButton button && button.Pressed
                 && button.ButtonIndex == MouseButton.Left && _hovered != null)
        {
            _tree.Toggle(_hovered);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_tree == null) return;

        Vector2 c = Center;
        float r = WheelRadius;
        var faint = new Color(1f, 1f, 1f, 0.25f);

        // Element wedges
        for (int i = 0; i < SkillNode.ElementCount; i++)
        {
            var points = new List<Vector2> { c };
            for (int s = 0; s <= 12; s++)
                points.Add(c + Dir(i * SkillNode.WedgeDegrees + s * SkillNode.WedgeDegrees / 12f) * r);

            Color fill = ElementColors[i];
            fill.A = 0.18f;
            DrawColoredPolygon(points.ToArray(), fill);
            DrawLine(c, c + Dir(i * SkillNode.WedgeDegrees) * r, faint, 1.5f);

            string name = ElementNames[i];
            Vector2 size = _font.GetStringSize(name, HorizontalAlignment.Left, -1, 16);
            Vector2 labelPos = c + Dir(i * SkillNode.WedgeDegrees + SkillNode.WedgeDegrees / 2f) * (r + 26f);
            DrawString(_font, labelPos + new Vector2(-size.X / 2f, 6f), name, HorizontalAlignment.Left, -1, 16, ElementColors[i]);
        }

        // Tier rings
        for (int t = 1; t <= 3; t++)
            DrawArc(c, r * t / 3f, 0f, Mathf.Tau, 96, faint, 1.5f);

        // Connections (root nodes connect to the core)
        foreach (SkillNode n in _tree.Nodes)
        {
            if (n.Prerequisites.Count == 0)
            {
                bool lit = _tree.IsUnlocked(n);
                DrawLine(c, NodePos(n), lit ? Colors.White : faint, lit ? 3f : 1.5f);
            }

            foreach (SkillNode p in n.Prerequisites)
            {
                bool lit = _tree.IsUnlocked(n) && _tree.IsUnlocked(p);
                DrawLine(NodePos(p), NodePos(n), lit ? Colors.White : faint, lit ? 3f : 1.5f);
            }
        }

        // Nodes
        foreach (SkillNode n in _tree.Nodes)
        {
            Vector2 p = NodePos(n);
            bool unlocked = _tree.IsUnlocked(n);
            bool available = _tree.CanUnlock(n);
            float size = n.IsFusion ? 12f : 9f;

            DrawCircle(p, size, unlocked ? ElementColors[SkillNode.WheelIndex(n.Element)] : new Color(0.12f, 0.12f, 0.14f));

            Color outline = n.IsFusion ? FusionRing
                : unlocked || available ? Colors.White
                : faint;
            DrawArc(p, size, 0f, Mathf.Tau, 24, outline, n == _hovered ? 3.5f : 2f);
        }

        DrawCircle(c, 16f, new Color(0.08f, 0.08f, 0.1f));
        DrawString(_font, new Vector2(24f, 36f), "Click a node to learn / unlearn (debug)",
            HorizontalAlignment.Left, -1, 14, new Color(1f, 1f, 1f, 0.6f));

        if (_hovered != null) DrawTooltip(_hovered);
    }

    private void DrawTooltip(SkillNode n)
    {
        var pos = new Vector2(24f, Size.Y - 150f);
        DrawRect(new Rect2(pos - new Vector2(12f, 30f), new Vector2(400f, 140f)), new Color(0f, 0f, 0f, 0.75f));

        DrawString(_font, pos, n.DisplayName, HorizontalAlignment.Left, -1, 20, Colors.White);

        string status = _tree.IsUnlocked(n) ? "Learned" : _tree.CanUnlock(n) ? "Available" : "Locked";
        DrawString(_font, pos + new Vector2(0f, 22f), status, HorizontalAlignment.Left, -1, 13, FusionRing);

        DrawMultilineString(_font, pos + new Vector2(0f, 46f), n.Description,
            HorizontalAlignment.Left, 376f, 14, -1, new Color(0.85f, 0.85f, 0.85f));
    }
}