using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.Core.Results;

public sealed record OperationResult<T>(T? Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => !Diagnostics.HasBlockingDiagnostics();

    public static OperationResult<T> Ok(T value, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(value, diagnostics ?? []);

    public static OperationResult<T> Fail(IReadOnlyList<Diagnostic> diagnostics) =>
        new(default, diagnostics);
}
