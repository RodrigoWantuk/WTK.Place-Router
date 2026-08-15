using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Rendering;
using PlaceRouter.Presentation.Selection;

namespace PlaceRouter.Presentation.ViewModels;

public sealed partial class BoardWorkspaceViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;
    private readonly SelectionCoordinator _selection;
    private readonly PcbSnapshotBuilder _snapshotBuilder;

    [ObservableProperty]
    private PcbBoardSnapshot _snapshot = PcbBoardSnapshot.Empty;

    [ObservableProperty]
    private double _zoom = 1;

    public BoardWorkspaceViewModel(ProjectCoordinator projects, SelectionCoordinator selection, PcbSnapshotBuilder? snapshotBuilder = null)
    {
        _projects = projects;
        _selection = selection;
        _snapshotBuilder = snapshotBuilder ?? new PcbSnapshotBuilder();
        _projects.ProjectChanged += (_, _) => Refresh();
        _selection.SelectionChanged += (_, _) => Refresh();
    }

    [RelayCommand]
    private void SelectEntity(PcbShapeSnapshot? shape)
    {
        if (shape is null)
        {
            _selection.Clear(SelectionOrigin.Viewport);
            return;
        }

        _selection.Select(new EntityReference(shape.EntityType, shape.EntityId), SelectionOrigin.Viewport);
    }

    [RelayCommand]
    private void FitBoard()
    {
        Zoom = 1;
    }

    public void Refresh()
    {
        Snapshot = _snapshotBuilder.Build(_projects.Project, _projects.ConstraintReport, _selection.Current.Items);
    }
}
