using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.Application.Projects;

public sealed record ProjectLoadResult(CanonicalProject? Project, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Project is not null && Diagnostics.All(d => !d.Blocking);
}

public sealed record ProjectSaveResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => !d.Blocking);
}

public sealed record ProjectValidationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => !d.Blocking);
}

public sealed record PrdxWriteOptions(Action<string>? BeforeCommit = null);

public interface IPrdxProjectReader
{
    ProjectLoadResult Load(string path);
}

public interface IPrdxProjectWriter
{
    ProjectSaveResult Save(CanonicalProject project, string path, PrdxWriteOptions? options = null);
}

public interface ICanonicalProjectValidator
{
    ProjectValidationResult Validate(CanonicalProject project);
}
