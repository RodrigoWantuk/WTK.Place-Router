using PlaceRouter.Application.Lifecycle;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Geometry;

namespace PlaceRouter.Presentation.Project;

public sealed class ProjectCoordinator
{
    private readonly ProjectService _projectService;
    private readonly ConstraintEvaluationService _evaluationService;
    private readonly List<string> _recentProjects = [];

    public ProjectCoordinator(ProjectService projectService, ConstraintEvaluationService? evaluationService = null)
    {
        _projectService = projectService;
        _evaluationService = evaluationService ?? new ConstraintEvaluationService();
    }

    public event EventHandler? ProjectChanged;

    public ProjectSession? Session { get; private set; }

    public ProjectDocument? Document => Session?.Document;

    public CanonicalProject? Project => Document?.Project;

    public string? CurrentPath { get; private set; }

    public bool IsDirty { get; private set; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

    public ConstraintEvaluationReport? ConstraintReport { get; private set; }

    public IReadOnlyList<string> RecentProjects => _recentProjects;

    public ProjectDocument NewProject(string name)
    {
        var document = _projectService.CreateProject(name);
        Attach(document, null, [], dirty: true);
        return document;
    }

    public ImportResult ImportDesign(string path, SourceRetentionPolicy retentionPolicy)
    {
        var result = _projectService.ImportDesign(new ImportRequest(path, retentionPolicy));
        if (result.Document is not null)
        {
            Attach(result.Document, null, result.Diagnostics, dirty: true);
        }
        else
        {
            Diagnostics = result.Diagnostics;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public ProjectLoadResult OpenProject(string path)
    {
        var result = _projectService.LoadProject(path);
        if (result.Document is not null)
        {
            Attach(result.Document, path, result.Diagnostics, dirty: false);
        }
        else
        {
            Diagnostics = result.Diagnostics;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public ProjectSaveResult SaveProject()
    {
        if (Document is null || string.IsNullOrWhiteSpace(CurrentPath))
        {
            var diagnostic = Diagnostic.Warning("UI-SAVE-PATH-MISSING", "Project", "Save As is required before saving this project.", blocking: true);
            Diagnostics = [diagnostic];
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return new ProjectSaveResult([diagnostic]);
        }

        return SaveProjectAs(CurrentPath);
    }

    public ProjectSaveResult SaveProjectAs(string path)
    {
        if (Document is null)
        {
            var diagnostic = Diagnostic.Warning("UI-NO-PROJECT", "Project", "No project is open.", blocking: true);
            Diagnostics = [diagnostic];
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return new ProjectSaveResult([diagnostic]);
        }

        var result = _projectService.SaveProject(Document, path);
        Diagnostics = result.Diagnostics;
        if (result.Success)
        {
            CurrentPath = path;
            IsDirty = false;
            AddRecent(path);
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void CloseProject()
    {
        Session = null;
        CurrentPath = null;
        IsDirty = false;
        Diagnostics = [];
        ConstraintReport = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadRecentProjects(IEnumerable<string> paths)
    {
        _recentProjects.Clear();
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
        {
            _recentProjects.Add(path);
        }
    }

    private void Attach(ProjectDocument document, string? path, IReadOnlyList<Diagnostic> diagnostics, bool dirty)
    {
        Session = new ProjectSession(document);
        CurrentPath = path;
        IsDirty = dirty;
        Diagnostics = diagnostics;
        ConstraintReport = _evaluationService.Evaluate(document.Project);
        if (!string.IsNullOrWhiteSpace(path))
        {
            AddRecent(path);
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddRecent(string path)
    {
        _recentProjects.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, path);
        if (_recentProjects.Count > 10)
        {
            _recentProjects.RemoveRange(10, _recentProjects.Count - 10);
        }
    }
}
