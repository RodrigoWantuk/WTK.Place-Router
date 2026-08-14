using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.Application.Projects;

public sealed class ProjectService(
    IPrdxProjectReader reader,
    IPrdxProjectWriter writer,
    ICanonicalProjectValidator validator)
{
    public CanonicalProject CreateProject(string name) => CanonicalProjectFactory.CreateEmpty(name);

    public ProjectLoadResult LoadProject(string path) => reader.Load(path);

    public ProjectSaveResult SaveProject(CanonicalProject project, string path, PrdxWriteOptions? options = null) =>
        writer.Save(project, path, options);

    public ProjectValidationResult ValidateProject(CanonicalProject project) => validator.Validate(project);
}
