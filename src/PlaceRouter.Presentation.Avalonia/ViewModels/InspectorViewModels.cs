using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Selection;

namespace PlaceRouter.Presentation.ViewModels;

public sealed record InspectorProperty(string Name, string Value);

public sealed partial class InspectorHostViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;
    private readonly SelectionCoordinator _selection;

    [ObservableProperty]
    private string _title = "Inspector";

    [ObservableProperty]
    private string _subtitle = "Nenhuma seleção";

    public ObservableCollection<InspectorProperty> Properties { get; } = [];

    public InspectorHostViewModel(ProjectCoordinator projects, SelectionCoordinator selection)
    {
        _projects = projects;
        _selection = selection;
        _projects.ProjectChanged += (_, _) => Refresh();
        _selection.SelectionChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        Properties.Clear();
        var project = _projects.Project;
        var selected = _selection.Current.Items;
        if (project is null)
        {
            Title = "Inspector";
            Subtitle = "Nenhum projeto aberto";
            return;
        }

        if (selected.Count == 0)
        {
            Title = project.Metadata.Name;
            Subtitle = "Projeto";
            Add("Schema", project.SchemaVersion);
            Add("Componentes", project.LogicalDesign.Components.Count.ToString());
            Add("Nets", project.LogicalDesign.Nets.Count.ToString());
            Add("Layers", project.Board.Layers.Count.ToString());
            return;
        }

        if (selected.Count > 1)
        {
            Title = "Seleção";
            Subtitle = $"{selected.Count} entidades";
            foreach (var item in selected)
            {
                Add(item.EntityType, item.EntityId);
            }

            return;
        }

        Inspect(project, selected[0]);
    }

    private void Inspect(CanonicalProject project, EntityReference reference)
    {
        Title = reference.EntityId;
        Subtitle = reference.EntityType;
        Add("Tipo", reference.EntityType);
        Add("Id", reference.EntityId);

        if (reference.EntityType.Equals("COMPONENT", StringComparison.OrdinalIgnoreCase))
        {
            var component = project.LogicalDesign.Components.FirstOrDefault(c => c.Id.Value == reference.EntityId);
            var pose = project.PhysicalDesignState.ComponentPoses.FirstOrDefault(p => p.ComponentId.Value == reference.EntityId);
            if (component is not null)
            {
                Title = component.ReferenceDesignator;
                Subtitle = component.Value ?? "Componente";
                Add("Footprint", component.FootprintId?.Value ?? "desconhecido");
                Add("Part number", component.PartNumber ?? "-");
                Add("Placement", component.PlacementPolicy);
            }

            if (pose is not null)
            {
                Add("X", pose.Position.X.ToString());
                Add("Y", pose.Position.Y.ToString());
                Add("Rotação", pose.Rotation.ToString());
                Add("Lado", pose.Side);
                Add("Estado", pose.PlacementState);
            }
        }
        else if (reference.EntityType.Equals("NET", StringComparison.OrdinalIgnoreCase))
        {
            var net = project.LogicalDesign.Nets.FirstOrDefault(n => n.Id.Value == reference.EntityId);
            if (net is not null)
            {
                Title = net.Name;
                Subtitle = "Net";
                Add("Classe", net.NetClassId?.Value ?? "-");
                Add("Endpoints", net.Endpoints.Count.ToString());
            }
        }
        else if (reference.EntityType.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
        {
            var constraint = project.Constraints.FirstOrDefault(c => c.Id.Value == reference.EntityId);
            if (constraint is not null)
            {
                Title = constraint.Type;
                Subtitle = constraint.Enforcement;
                Add("Habilitada", constraint.Enabled ? "Sim" : "Não");
                Add("Source", FormatSelector(constraint.Source));
                Add("Target", constraint.Target is null ? "-" : FormatSelector(constraint.Target));
            }
        }
    }

    private void Add(string name, string value) => Properties.Add(new InspectorProperty(name, value));

    private static string FormatSelector(ConstraintSelector selector) =>
        selector.Kind.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? "ALL"
            : $"{selector.EntityType}: {string.Join(", ", selector.EntityIds)}";
}

public sealed partial class ConstraintComposerViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;
    private readonly SelectionCoordinator _selection;

    [ObservableProperty]
    private string _summary = "Nenhuma seleção.";

    [ObservableProperty]
    private string _source = "-";

    [ObservableProperty]
    private string _target = "-";

    [ObservableProperty]
    private string _constraintType = "MinimumSeparation";

    [ObservableProperty]
    private string _enforcement = "REQUIRED";

    public ConstraintComposerViewModel(ProjectCoordinator projects, SelectionCoordinator selection)
    {
        _projects = projects;
        _selection = selection;
        _projects.ProjectChanged += (_, _) => Refresh();
        _selection.SelectionChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var selected = _selection.Current.Items;
        Source = selected.ElementAtOrDefault(0) is { } source ? $"{source.EntityType}:{source.EntityId}" : "-";
        Target = selected.ElementAtOrDefault(1) is { } target ? $"{target.EntityType}:{target.EntityId}" : "-";
        Summary = _projects.Project is null
            ? "Nenhum projeto carregado."
            : $"{_projects.Project.Constraints.Count} constraint(s) no projeto; nova sugestão: {ConstraintType} {Enforcement}.";
    }
}
