using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Application.Projects;

public sealed record ProjectDocument(CanonicalProject Project, ProjectFileContext FileContext)
{
    public static ProjectDocument New(CanonicalProject project) =>
        new(project, ProjectFileContext.New());
}

public sealed record ProjectFileContext(
    string? SourcePath,
    string FormatVersion,
    IReadOnlyList<string> FeatureFlags,
    IReadOnlyList<SourceFingerprint> SourceFingerprints,
    IReadOnlyList<SupplementaryEntry> SupplementaryEntries,
    IReadOnlyList<PendingSupplementaryFile> PendingSupplementaryFiles)
{
    public static ProjectFileContext New() => new(null, "0.1.0", [], [], [], []);
}

public sealed record SourceFingerprint(SourceImportId SourceImportId, string Sha256);

public sealed record SupplementaryEntry(string Path, long Length, string Sha256);

public sealed record PendingSupplementaryFile(string SourcePath, string EntryPath);

public sealed record ProjectLoadResult(ProjectDocument? Document, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Document is not null && !Diagnostics.HasBlockingDiagnostics();
    public CanonicalProject? Project => Document?.Project;
}

public sealed record ProjectSaveResult(IReadOnlyList<Diagnostic> Diagnostics, ProjectDocument? Document = null)
{
    public bool Success => !Diagnostics.HasBlockingDiagnostics();
}

public sealed record ProjectValidationResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => !Diagnostics.HasBlockingDiagnostics();
}

public interface IProjectStore
{
    ProjectLoadResult Load(string path);

    ProjectSaveResult Save(ProjectDocument document, string path);
}

public interface ICanonicalProjectValidator
{
    ProjectValidationResult Validate(CanonicalProject project);
}
