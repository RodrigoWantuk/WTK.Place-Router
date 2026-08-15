using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Selection;

namespace PlaceRouter.Presentation.ViewModels;

public sealed partial class NavigatorItemViewModel : ViewModelBase
{
    public NavigatorItemViewModel(string entityType, string entityId, string title, string subtitle, string badge = "")
    {
        EntityType = entityType;
        EntityId = entityId;
        Title = title;
        Subtitle = subtitle;
        Badge = badge;
    }

    public string EntityType { get; }
    public string EntityId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Badge { get; }
}

public sealed partial class DesignNavigatorViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;
    private readonly SelectionCoordinator _selection;

    public DesignNavigatorViewModel(ProjectCoordinator projects, SelectionCoordinator selection)
    {
        _projects = projects;
        _selection = selection;
        _projects.ProjectChanged += (_, _) => Refresh();
    }

    public ObservableCollection<NavigatorItemViewModel> Components { get; } = [];
    public ObservableCollection<NavigatorItemViewModel> Nets { get; } = [];
    public ObservableCollection<NavigatorItemViewModel> Groups { get; } = [];
    public ObservableCollection<NavigatorItemViewModel> Constraints { get; } = [];

    [RelayCommand]
    private void Select(NavigatorItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _selection.Select(new EntityReference(item.EntityType, item.EntityId), SelectionOrigin.Navigator);
    }

    public void Refresh()
    {
        Components.Clear();
        Nets.Clear();
        Groups.Clear();
        Constraints.Clear();

        var project = _projects.Project;
        if (project is null)
        {
            return;
        }

        foreach (var component in project.LogicalDesign.Components.OrderBy(static c => c.ReferenceDesignator, StringComparer.OrdinalIgnoreCase))
        {
            var pose = project.PhysicalDesignState.ComponentPoses.FirstOrDefault(p => p.ComponentId == component.Id);
            Components.Add(new NavigatorItemViewModel(
                "COMPONENT",
                component.Id.Value,
                component.ReferenceDesignator,
                component.Value ?? component.PartNumber ?? "Componente",
                pose?.PlacementState ?? component.PlacementPolicy));
        }

        foreach (var net in project.LogicalDesign.Nets.OrderBy(static n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            Nets.Add(new NavigatorItemViewModel("NET", net.Id.Value, net.Name, $"{net.Endpoints.Count} endpoint(s)", net.NetClassId?.Value ?? ""));
        }

        foreach (var group in project.LogicalDesign.Groups.OrderBy(static g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new NavigatorItemViewModel("GROUP", group.Id.Value, group.Name, group.GroupType, $"{group.Members.Count} item(s)"));
        }

        foreach (var constraint in project.Constraints.OrderBy(static c => c.Type, StringComparer.OrdinalIgnoreCase))
        {
            Constraints.Add(new NavigatorItemViewModel("CONSTRAINT", constraint.Id.Value, constraint.Type, constraint.Enforcement, constraint.Enabled ? "on" : "off"));
        }
    }
}
