using System.Text.Json;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public sealed record ManufacturingLengthRule(
    string Name,
    LengthUnits? Value,
    string Status,
    Provenance Provenance,
    IReadOnlyList<string> SourceKeys)
{
    public bool HasKnownValue =>
        Value is not null &&
        !string.Equals(Status, "UNKNOWN", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "NOT_APPLICABLE", StringComparison.OrdinalIgnoreCase);
}

public sealed record ManufacturingSetRule(
    string Name,
    IReadOnlySet<string>? Values,
    string Status,
    Provenance Provenance,
    IReadOnlyList<string> SourceKeys)
{
    public bool HasKnownValue =>
        Values is not null &&
        !string.Equals(Status, "UNKNOWN", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "NOT_APPLICABLE", StringComparison.OrdinalIgnoreCase);
}

public sealed record ManufacturingRules(
    ManufacturingLengthRule MinimumTrackWidth,
    ManufacturingLengthRule MinimumClearance,
    ManufacturingLengthRule MinimumDrill,
    ManufacturingLengthRule MinimumViaDiameter,
    ManufacturingLengthRule AnnularRing,
    ManufacturingLengthRule CopperToEdge,
    ManufacturingLengthRule MinimumComponentSpacing,
    ManufacturingSetRule AllowedViaTypes,
    ManufacturingSetRule AllowedLayerCounts,
    Provenance Provenance);

public static class ManufacturingRuleResolver
{
    public static ManufacturingRules Resolve(ManufacturingProfile profile) =>
        new(
            Length(profile, "MinimumTrackWidth", "minimumTrackWidth", "minimumTraceWidth", "minimumWidth"),
            Length(profile, "MinimumClearance", "minimumSpacing", "minimumClearance"),
            Length(profile, "MinimumDrill", "minimumDrill"),
            Length(profile, "MinimumViaDiameter", "minimumViaDiameter"),
            Length(profile, "AnnularRing", "minimumAnnularRing", "annularRing"),
            Length(profile, "CopperToEdge", "copperToEdge", "minimumCopperToEdge"),
            Length(profile, "MinimumComponentSpacing", "minimumComponentSpacing", "minimumCourtyardSpacing"),
            StringSet(profile, "AllowedViaTypes", "allowedViaTypes"),
            StringSet(profile, "AllowedLayerCounts", "allowedLayerCounts", "allowedCopperLayerCounts"),
            profile.Provenance);

    private static ManufacturingLengthRule Length(ManufacturingProfile profile, string name, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (profile.Capabilities.TryGetValue(key, out var value) && TryReadLong(value.Value, out var units))
            {
                return new ManufacturingLengthRule(name, new LengthUnits(units), NormalizeStatus(value.Status), value.Provenance, [key]);
            }
        }

        return new ManufacturingLengthRule(name, null, "UNKNOWN", profile.Provenance, keys);
    }

    private static ManufacturingSetRule StringSet(ManufacturingProfile profile, string name, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!profile.Capabilities.TryGetValue(key, out var value))
            {
                continue;
            }

            var set = ReadStringSet(value.Value);
            if (set is not null)
            {
                return new ManufacturingSetRule(name, set, NormalizeStatus(value.Status), value.Provenance, [key]);
            }
        }

        return new ManufacturingSetRule(name, null, "UNKNOWN", profile.Provenance, keys);
    }

    private static IReadOnlySet<string>? ReadStringSet(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(ReadString)
                .OfType<string>()
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var scalar = ReadString(element);
        return string.IsNullOrWhiteSpace(scalar)
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { scalar };
    }

    private static string? ReadString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };

    private static string NormalizeStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.ToUpperInvariant();

    private static bool TryReadLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}

public static class Plan02DiagnosticCodes
{
    public const string ConstraintConflict = "P02-CONSTRAINT-CONFLICT";
    public const string ConstraintFailed = "P02-CONSTRAINT-FAILED";
    public const string ConstraintUnknown = "P02-CONSTRAINT-UNKNOWN";
    public const string ReadinessMissing = "P02-READINESS-MISSING";
}
