using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Selection;

namespace PlaceRouter.Presentation.ViewModels;

public sealed partial class WorkbenchItemViewModel : ViewModelBase
{
    public WorkbenchItemViewModel(string severity, string title, string message, IReadOnlyList<EntityReference> affectedEntities)
    {
        Severity = severity;
        Title = title;
        Message = message;
        AffectedEntities = affectedEntities;
    }

    public string Severity { get; }
    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<EntityReference> AffectedEntities { get; }
}

public sealed partial class BottomWorkbenchViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;
    private readonly SelectionCoordinator _selection;

    public BottomWorkbenchViewModel(ProjectCoordinator projects, SelectionCoordinator selection)
    {
        _projects = projects;
        _selection = selection;
        _projects.ProjectChanged += (_, _) => Refresh();
    }

    public ObservableCollection<WorkbenchItemViewModel> Findings { get; } = [];
    public ObservableCollection<WorkbenchItemViewModel> Diagnostics { get; } = [];
    public ObservableCollection<WorkbenchItemViewModel> Metrics { get; } = [];
    public ObservableCollection<WorkbenchItemViewModel> Routing { get; } = [];

    [RelayCommand]
    private void SelectFinding(WorkbenchItemViewModel? item)
    {
        if (item?.AffectedEntities.Count > 0)
        {
            _selection.Select(item.AffectedEntities, SelectionOrigin.Workbench);
        }
    }

    public void Refresh()
    {
        Findings.Clear();
        Diagnostics.Clear();
        Metrics.Clear();
        Routing.Clear();

        foreach (var finding in _projects.ConstraintReport?.Findings ?? [])
        {
            Findings.Add(new WorkbenchItemViewModel(finding.Severity, finding.Category, finding.Message, finding.AffectedEntities));
        }

        foreach (var diagnostic in _projects.Diagnostics)
        {
            Diagnostics.Add(new WorkbenchItemViewModel(diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message, diagnostic.EntityRefs ?? []));
        }

        if (_projects.Project is { } project)
        {
            var summary = project.Summary;
            Metrics.Add(new WorkbenchItemViewModel("Info", "Resumo", $"{summary.Components} componentes, {summary.Nets} nets, {summary.Routes} rotas, {summary.Vias} vias.", []));
            Metrics.Add(new WorkbenchItemViewModel("Info", "Readiness", _projects.ConstraintReport?.SummaryLine() ?? "Sem avaliação.", []));
            Routing.Add(new WorkbenchItemViewModel("Info", "Routing", $"{project.PhysicalDesignState.Routes.Count} route(s), {project.PhysicalDesignState.Vias.Count} via(s).", []));
        }
    }
}
