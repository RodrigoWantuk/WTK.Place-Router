using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Application.Lifecycle;

namespace PlaceRouter.Application.Projects;

public sealed class ProjectService(
    IProjectStore store,
    ICanonicalProjectValidator validator,
    IEnumerable<IDesignImporter>? importers = null)
{
    private readonly IReadOnlyList<IDesignImporter> _importers = (importers ?? []).ToArray();

    public ProjectDocument CreateProject(string name) =>
        ProjectDocument.New(CanonicalProjectFactory.CreateIncomplete(name));

    public ProjectLoadResult LoadProject(string path) => store.Load(path);

    public ProjectSaveResult SaveProject(ProjectDocument document, string path) =>
        store.Save(document, path);

    public ProjectSaveResult SaveSession(ProjectSession session, string path) =>
        session.Save(store, path);

    public ProjectValidationResult ValidateProject(CanonicalProject project) => validator.Validate(project);

    public ImportResult ImportDesign(ImportRequest request)
    {
        var importer = _importers.FirstOrDefault(i => i.CanImport(request));
        if (importer is null)
        {
            var diagnostic = Diagnostic.Fatal(
                DiagnosticCodes.ImportUnsupported,
                "Import",
                $"No importer is registered for '{request.SourcePath}'.");
            return new ImportResult(null, new Dictionary<string, string>(StringComparer.Ordinal), [diagnostic], null, new ImportLossReport([]));
        }

        var imported = importer.Import(request);
        if (!imported.Success || imported.Document is null)
        {
            return imported;
        }

        var validation = validator.Validate(imported.Document.Project);
        var diagnostics = imported.Diagnostics.Concat(validation.Diagnostics).ToArray();
        return imported with { Diagnostics = diagnostics };
    }
}
