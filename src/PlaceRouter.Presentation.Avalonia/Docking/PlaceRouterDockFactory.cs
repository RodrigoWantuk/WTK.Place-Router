using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using PlaceRouter.Presentation.ViewModels;
using Alignment = Dock.Model.Core.Alignment;
using Orientation = Dock.Model.Core.Orientation;

namespace PlaceRouter.Presentation.Docking;

public sealed class PlaceRouterDockFactory : Factory
{
    private readonly PlaceRouterShellViewModel _shell;

    public PlaceRouterDockFactory(PlaceRouterShellViewModel shell)
    {
        _shell = shell;
    }

    public override IRootDock CreateLayout()
    {
        var navigatorDock = CreateToolDock(
            "dock.navigator",
            Alignment.Left,
            _shell.LeftLayoutProportion,
            CreateTool("tool.navigator", "Design Navigator", _shell.DesignNavigator, minWidth: 250));

        var composerDock = CreateToolDock(
            "dock.constraints",
            Alignment.Right,
            _shell.ComposerLayoutProportion,
            CreateTool("tool.constraints", "Constraints", _shell.ConstraintComposer, minWidth: 320));

        var inspectorDock = CreateToolDock(
            "dock.inspector",
            Alignment.Right,
            _shell.InspectorLayoutProportion,
            CreateTool("tool.inspector", "Inspector", _shell.Inspector, minWidth: 320));

        var workbenchDock = CreateToolDock(
            "dock.workbench",
            Alignment.Bottom,
            _shell.BottomLayoutProportion,
            CreateTool("tool.workbench", "Workbench", _shell.BottomWorkbench, minHeight: 180));

        var board = new Document
        {
            Id = "document.board",
            Title = "PCB",
            Context = _shell.BoardWorkspace,
            CanClose = false,
            CanFloat = false,
            CanDrag = false
        };

        var documentDock = new DocumentDock
        {
            Id = "dock.documents",
            Title = "Board",
            CanCloseLastDockable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>(board),
            ActiveDockable = board,
            DefaultDockable = board
        };

        var rightDock = new ProportionalDock
        {
            Id = "dock.right",
            Orientation = Orientation.Vertical,
            Proportion = _shell.RightLayoutProportion,
            VisibleDockables = CreateList<IDockable>(
                composerDock,
                new ProportionalDockSplitter(),
                inspectorDock),
            ActiveDockable = inspectorDock
        };

        var centerDock = new ProportionalDock
        {
            Id = "dock.center",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter(),
                workbenchDock),
            ActiveDockable = documentDock
        };

        var mainDock = new ProportionalDock
        {
            Id = "dock.main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                navigatorDock,
                new ProportionalDockSplitter(),
                centerDock,
                new ProportionalDockSplitter(),
                rightDock),
            ActiveDockable = centerDock
        };

        var root = CreateRootDock();
        root.Id = "dock.root";
        root.Title = "WTK Place&Router";
        root.VisibleDockables = CreateList<IDockable>(mainDock);
        root.ActiveDockable = mainDock;
        root.DefaultDockable = mainDock;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => DockSettings.UseManagedWindows
                ? new ManagedHostWindow()
                : new HostWindow()
        };

        base.InitLayout(layout);
    }

    private ToolDock CreateToolDock(string id, Alignment alignment, double proportion, Tool tool)
    {
        return new ToolDock
        {
            Id = id,
            Title = tool.Title,
            Alignment = alignment,
            Proportion = proportion,
            VisibleDockables = CreateList<IDockable>(tool),
            ActiveDockable = tool,
            DefaultDockable = tool
        };
    }

    private static Tool CreateTool(string id, string title, object context, double minWidth = 0, double minHeight = 0)
    {
        return new Tool
        {
            Id = id,
            Title = title,
            Context = context,
            CanClose = false,
            CanFloat = true,
            CanPin = true,
            CanDrag = true,
            CanDrop = true,
            MinWidth = minWidth,
            MinHeight = minHeight
        };
    }
}
