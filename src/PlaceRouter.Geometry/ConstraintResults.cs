using PlaceRouter.Core.Primitives;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public enum ConstraintEvaluationStatus
{
    Pass,
    Fail,
    Unknown,
    NotApplicable
}

public enum ReadinessStatus
{
    Ready,
    ReadyWithWarnings,
    Blocked
}

public sealed record ConstraintEvidence(
    IReadOnlyDictionary<string, object?> Values)
{
    public static ConstraintEvidence Empty { get; } = new(new Dictionary<string, object?>());

    public static ConstraintEvidence From(params (string Key, object? Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal));
}

public sealed record ConstraintEvaluation(
    ConstraintId ConstraintId,
    string ConstraintType,
    string Enforcement,
    ConstraintEvaluationStatus Status,
    IReadOnlyList<EntityReference> AffectedEntities,
    LengthUnits? RequiredUnits,
    LengthUnits? ActualUnits,
    ConstraintEvidence Evidence,
    Provenance Provenance,
    string? Message)
{
    public bool BlocksCandidate =>
        Status == ConstraintEvaluationStatus.Fail &&
        string.Equals(Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase);
}

public sealed record ConstraintConflict(
    ConstraintId FirstConstraintId,
    ConstraintId SecondConstraintId,
    string Message,
    ConstraintEvidence Evidence);

public sealed record PhysicalFinding(
    string Id,
    string Severity,
    string Category,
    string Message,
    IReadOnlyList<EntityReference> AffectedEntities,
    ConstraintEvidence Evidence,
    string Source,
    string Status);

public sealed record ConstraintEvaluationReport(
    IReadOnlyList<ConstraintEvaluation> Evaluations,
    IReadOnlyList<ConstraintConflict> Conflicts,
    ReadinessReport Readiness,
    IReadOnlyList<PhysicalFinding> Findings)
{
    public bool CandidateValid =>
        Conflicts.Count == 0 &&
        !Evaluations.Any(e => e.BlocksCandidate) &&
        Readiness.Status != ReadinessStatus.Blocked;

    public string SummaryLine()
    {
        var required = Evaluations.Where(e => string.Equals(e.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase)).ToArray();
        var preferredViolations = Evaluations.Count(e => string.Equals(e.Enforcement, "PREFERRED", StringComparison.OrdinalIgnoreCase) && e.Status == ConstraintEvaluationStatus.Fail);
        return $"Geometry objects indexed: {Readiness.GeometryObjectsIndexed}; Required constraints: {required.Count(e => e.Status == ConstraintEvaluationStatus.Pass)} PASS / {required.Count(e => e.Status == ConstraintEvaluationStatus.Fail)} FAIL / {required.Count(e => e.Status == ConstraintEvaluationStatus.Unknown)} UNKNOWN; Preferences: {preferredViolations} violations; Readiness: {Readiness.Status}";
    }
}
