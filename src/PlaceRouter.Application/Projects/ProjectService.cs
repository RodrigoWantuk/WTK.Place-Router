using PlaceRouter.Domain.Model;

namespace PlaceRouter.Application.Projects;

public sealed class ProjectService(
    IProjectStore store,
    ICanonicalProjectValidator validator)
{
    public ProjectDocument CreateProject(string name) =>
        ProjectDocument.New(CanonicalProjectFactory.CreateIncomplete(name));

    public ProjectLoadResult LoadProject(string path) => store.Load(path);

    public ProjectSaveResult SaveProject(ProjectDocument document, string path) =>
        store.Save(document, path);

    public ProjectValidationResult ValidateProject(CanonicalProject project) => validator.Validate(project);
}
