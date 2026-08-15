using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Application.Projects;

public enum SourceRetentionPolicy
{
    Embed,
    ReferenceOnly,
    None
}

public sealed record ImportRequest(
    string SourcePath,
    SourceRetentionPolicy SourceRetentionPolicy = SourceRetentionPolicy.ReferenceOnly,
    IReadOnlyDictionary<string, string>? Options = null);

public sealed record ImportLossReport(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasLoss => Diagnostics.Count > 0;
}

public sealed record ImportResult(
    ProjectDocument? Document,
    IReadOnlyDictionary<string, string> Capabilities,
    IReadOnlyList<Diagnostic> Diagnostics,
    SourceFingerprint? SourceFingerprint,
    ImportLossReport LossReport)
{
    public bool Success => Document is not null && !Diagnostics.HasBlockingDiagnostics();
    public CanonicalProject? Project => Document?.Project;
}

public interface IDesignImporter
{
    string AdapterId { get; }

    string AdapterVersion { get; }

    string SourceType { get; }

    bool CanImport(ImportRequest request);

    ImportResult Import(ImportRequest request);
}
