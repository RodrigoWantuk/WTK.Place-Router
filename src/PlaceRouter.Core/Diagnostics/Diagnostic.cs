namespace PlaceRouter.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}

public sealed record EntityReference(string EntityType, string EntityId);

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Category,
    string Message,
    IReadOnlyList<EntityReference>? EntityRefs = null,
    IReadOnlyDictionary<string, object?>? Evidence = null,
    string? Remediation = null,
    string? Source = null,
    bool Blocking = false)
{
    public static Diagnostic Info(string code, string category, string message) =>
        new(code, DiagnosticSeverity.Info, category, message, Blocking: false);

    public static Diagnostic Error(string code, string category, string message, params EntityReference[] refs) =>
        new(code, DiagnosticSeverity.Error, category, message, refs, Blocking: true);

    public static Diagnostic Fatal(string code, string category, string message) =>
        new(code, DiagnosticSeverity.Fatal, category, message, Blocking: true);

    public static Diagnostic Warning(string code, string category, string message, bool blocking = false) =>
        new(code, DiagnosticSeverity.Warning, category, message, Blocking: blocking);
}

public static class DiagnosticCodes
{
    public const string ContainerInvalid = "PRDX-CONTAINER-INVALID";
    public const string EntryDuplicate = "PRDX-ENTRY-DUPLICATE";
    public const string EntryTooLarge = "PRDX-ENTRY-TOO-LARGE";
    public const string Utf8Invalid = "PRDX-UTF8-INVALID";
    public const string ManifestMissing = "PRDX-MANIFEST-MISSING";
    public const string ManifestSchema = "PRDX-MANIFEST-SCHEMA";
    public const string VersionUnsupported = "PRDX-VERSION-UNSUPPORTED";
    public const string FeatureUnsupported = "PRDX-FEATURE-UNSUPPORTED";
    public const string ManifestProjectMismatch = "PRDX-MANIFEST-PROJECT-MISMATCH";
    public const string SupplementaryMissing = "PRDX-SUPPLEMENTARY-MISSING";
    public const string PayloadMissing = "PRDX-PAYLOAD-MISSING";
    public const string PayloadHash = "PRDX-PAYLOAD-HASH";
    public const string ProjectSchema = "PRDX-PROJECT-SCHEMA";
    public const string RefNotFound = "PRDX-REF-NOT-FOUND";
    public const string DuplicateId = "PRDX-DUPLICATE-ID";
    public const string LayerNotFound = "PRDX-LAYER-NOT-FOUND";
    public const string PadFootprintMismatch = "PRDX-PAD-FOOTPRINT-MISMATCH";
    public const string PadMappingUnresolved = "PRDX-PAD-MAPPING-UNRESOLVED";
    public const string FootprintUnresolved = "PRDX-FOOTPRINT-UNRESOLVED";
    public const string GroupCycle = "PRDX-GROUP-CYCLE";
    public const string RefdesDuplicate = "PRDX-REFDES-DUPLICATE";
    public const string RouteViaNetMismatch = "PRDX-ROUTE-VIA-NET-MISMATCH";
    public const string LayerNotCopper = "PRDX-LAYER-NOT-COPPER";
    public const string SaveFailed = "PRDX-SAVE-FAILED";
}

public static class DiagnosticExtensions
{
    public static bool HasBlockingDiagnostics(this IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(d => d.Blocking);
}
