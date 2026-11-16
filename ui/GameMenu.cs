using Godot;

public partial class GameMenu : CanvasLayer
{
    [Export] public SkillTree SkillTree;

    private enum Page { Closed, Hub, Magic, Wip }

    private Page _page = Page.Closed;
    private Control _hub, _magic, _wip;

    public override void _Ready()
    {
        _hub = GetNode<Control>("%Hub");
        _wip = GetNode<Control>("%WipPage");

        var treeView = GetNode<SkillTreeView>("%MagicPage");
        treeView.SetTree(SkillTree);
        _magic = treeView;

        GetNode<Button>("%MagicButton").Pressed += () => ShowPage(Page.Magic);
        GetNode<Button>("%PhysicalButton").Pressed += () => ShowPage(Page.Wip);
        GetNode<Button>("%InventoryButton").Pressed += () => ShowPage(Page.Wip);

        ShowPage(Page.Closed);
    }

    public override void _Input(InputEvent e)
    {
        if (e.IsActionPressed("game_menu"))
        {
            if (_page == Page.Closed)
            {
                if (!GameState.MenuOpen) Open();
            }
            else
            {
                Close();
            }
            GetViewport().SetInputAsHandled();
        }
        else if (_page != Page.Closed && e.IsActionPressed("ui_cancel"))
        {
            // Esc steps back: page to hub, hub to closed
            if (_page == Page.Hub) Close();
            else ShowPage(Page.Hub);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Open()
    {
        ShowPage(Page.Hub); 
        GameState.MenuOpen = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void Close()
    {
        ShowPage(Page.Closed); GameState.MenuOpen = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ShowPage(Page page)
    {
        _page = page;
        Visible = page != Page.Closed;
        _hub.Visible = page == Page.Hub;
        _magic.Visible = page == Page.Magic;
        _wip.Visible = page == Page.Wip;
    }
}