using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.Core.Results;

public sealed record OperationResult<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => !d.Blocking || d.Severity is DiagnosticSeverity.Info or DiagnosticSeverity.Warning);

    public static OperationResult<T> Ok(T value, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(value, diagnostics ?? []);

    public static OperationResult<T> Fail(IReadOnlyList<Diagnostic> diagnostics) =>
        new(default, diagnostics);
}
