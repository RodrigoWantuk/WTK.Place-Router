using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using PlaceRouter.Application.Projects;
using PlaceRouter.Presentation.Docking;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Rendering;
using PlaceRouter.Presentation.Selection;
using PlaceRouter.Presentation.Workspace;

namespace PlaceRouter.Presentation.ViewModels;

public sealed partial class PlaceRouterShellViewModel : ViewModelBase
{
    private readonly PlaceRouterLayoutService _layoutService;
    private readonly PlaceRouterLayoutDocument _layoutDocument;

    [ObservableProperty]
    private string _title = "WTK Place&Router";

    [ObservableProperty]
    private IRootDock? _dockLayout;

    public PlaceRouterShellViewModel(ProjectCoordinator projects, PlaceRouterLayoutService layoutService)
    {
        Projects = projects;
        _layoutService = layoutService;
        _layoutDocument = _layoutService.Load();
        Projects.LoadRecentProjects(_layoutDocument.RecentProjects);

        Selection = new SelectionCoordinator();
        DesignNavigator = new DesignNavigatorViewModel(Projects, Selection);
        BoardWorkspace = new BoardWorkspaceViewModel(Projects, Selection, new PcbSnapshotBuilder());
        ConstraintComposer = new ConstraintComposerViewModel(Projects, Selection);
        Inspector = new InspectorHostViewModel(Projects, Selection);
        BottomWorkbench = new BottomWorkbenchViewModel(Projects, Selection);
        StatusBar = new StatusBarViewModel(Projects);
        DockFactory = new PlaceRouterDockFactory(this);
        DockLayout = DockFactory.CreateLayout();
        DockFactory.InitLayout(DockLayout);

        Projects.ProjectChanged += (_, _) => RefreshTitle();
        RefreshTitle();
    }

    public ProjectCoordinator Projects { get; }
    public SelectionCoordinator Selection { get; }
    public PlaceRouterDockFactory DockFactory { get; }
    public DesignNavigatorViewModel DesignNavigator { get; }
    public BoardWorkspaceViewModel BoardWorkspace { get; }
    public ConstraintComposerViewModel ConstraintComposer { get; }
    public InspectorHostViewModel Inspector { get; }
    public BottomWorkbenchViewModel BottomWorkbench { get; }
    public StatusBarViewModel StatusBar { get; }

    public double LeftLayoutProportion => _layoutDocument.Layout.LeftProportion;
    public double RightLayoutProportion => _layoutDocument.Layout.RightProportion;
    public double BottomLayoutProportion => _layoutDocument.Layout.BottomProportion;
    public double ComposerLayoutProportion => _layoutDocument.Layout.ComposerProportion;
    public double InspectorLayoutProportion => _layoutDocument.Layout.InspectorProportion;

    [RelayCommand]
    public void NewProject() => Projects.NewProject($"New Board {DateTimeOffset.Now:yyyyMMdd-HHmm}");

    [RelayCommand]
    public void CloseProject() => Projects.CloseProject();

    [RelayCommand]
    public void ResetLayout()
    {
        DockLayout = DockFactory.CreateLayout();
        if (DockLayout is not null)
        {
            DockFactory.InitLayout(DockLayout);
        }
    }

    public void ImportDsn(string path) => Projects.ImportDesign(path, SourceRetentionPolicy.ReferenceOnly);

    public void OpenProject(string path) => Projects.OpenProject(path);

    public bool SaveProject() => Projects.SaveProject().Success;

    public bool SaveProjectAs(string path) => Projects.SaveProjectAs(path).Success;

    public void PersistWorkspace(IReadOnlyList<PlaceRouterMonitorWorkArea> monitors)
    {
        _layoutDocument.RecentProjects = Projects.RecentProjects.ToList();
        if (DockLayout is not null)
        {
            _layoutDocument.Layout.FloatingDocks = PlaceRouterDockLayoutState.Capture(DockLayout, monitors).ToList();
        }

        _layoutService.Save(_layoutDocument);
    }

    private void RefreshTitle()
    {
        var suffix = Projects.IsDirty ? "*" : string.Empty;
        Title = Projects.Project is null
            ? "WTK Place&Router"
            : $"{Projects.Project.Metadata.Name}{suffix} - WTK Place&Router";
    }
}
