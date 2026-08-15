using System.Text.Json;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public sealed record ManufacturingRules(
    LengthUnits MinimumTrackWidth,
    LengthUnits MinimumClearance,
    LengthUnits MinimumDrill,
    LengthUnits MinimumViaDiameter,
    LengthUnits AnnularRing,
    LengthUnits CopperToEdge,
    LengthUnits MinimumComponentSpacing,
    IReadOnlySet<string> AllowedViaTypes,
    Provenance Provenance)
{
    public static ManufacturingRules ConservativeDefault(Provenance provenance) =>
        new(
            new LengthUnits(150),
            new LengthUnits(150),
            new LengthUnits(300),
            new LengthUnits(600),
            new LengthUnits(150),
            new LengthUnits(250),
            new LengthUnits(150),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "THROUGH" },
            provenance);
}

public static class ManufacturingRuleResolver
{
    public static ManufacturingRules Resolve(ManufacturingProfile profile)
    {
        var defaults = ManufacturingRules.ConservativeDefault(profile.Provenance);
        return defaults with
        {
            MinimumTrackWidth = Length(profile, "minimumTrackWidth") ?? defaults.MinimumTrackWidth,
            MinimumClearance = Length(profile, "minimumSpacing", "minimumClearance") ?? defaults.MinimumClearance,
            MinimumDrill = Length(profile, "minimumDrill") ?? defaults.MinimumDrill,
            MinimumViaDiameter = Length(profile, "minimumViaDiameter") ?? defaults.MinimumViaDiameter,
            AnnularRing = Length(profile, "minimumAnnularRing", "annularRing") ?? defaults.AnnularRing,
            CopperToEdge = Length(profile, "copperToEdge", "minimumCopperToEdge") ?? defaults.CopperToEdge,
            MinimumComponentSpacing = Length(profile, "minimumComponentSpacing", "minimumCourtyardSpacing") ?? defaults.MinimumComponentSpacing,
            AllowedViaTypes = StringSet(profile, "allowedViaTypes") ?? defaults.AllowedViaTypes,
            Provenance = profile.Provenance
        };
    }

    private static LengthUnits? Length(ManufacturingProfile profile, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (profile.Capabilities.TryGetValue(key, out var value) && IsUsable(value) && TryReadLong(value.Value, out var units))
            {
                return new LengthUnits(units);
            }
        }

        return null;
    }

    private static IReadOnlySet<string>? StringSet(ManufacturingProfile profile, string key)
    {
        if (!profile.Capabilities.TryGetValue(key, out var value) || !IsUsable(value) || value.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.Value.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUsable(SourcedValue value) =>
        !string.Equals(value.Status, "UNKNOWN", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value.Status, "NOT_APPLICABLE", StringComparison.OrdinalIgnoreCase);

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
