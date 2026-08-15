using CommunityToolkit.Mvvm.ComponentModel;
using PlaceRouter.Presentation.Project;

namespace PlaceRouter.Presentation.ViewModels;

public sealed partial class StatusBarViewModel : ViewModelBase
{
    private readonly ProjectCoordinator _projects;

    [ObservableProperty]
    private string _projectName = "Nenhum projeto";

    [ObservableProperty]
    private string _readiness = "Sem avaliação";

    [ObservableProperty]
    private string _candidate = "-";

    [ObservableProperty]
    private string _diagnostics = "0 diagnostics";

    public StatusBarViewModel(ProjectCoordinator projects)
    {
        _projects = projects;
        _projects.ProjectChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        ProjectName = _projects.Project?.Metadata.Name ?? "Nenhum projeto";
        var report = _projects.ConstraintReport;
        Readiness = report?.Readiness.Status.ToString() ?? "Sem avaliação";
        Candidate = report is null ? "-" : report.CandidateValid ? "Candidato válido" : "Candidato bloqueado";
        Diagnostics = $"{_projects.Diagnostics.Count} diagnostic(s)";
    }
}
