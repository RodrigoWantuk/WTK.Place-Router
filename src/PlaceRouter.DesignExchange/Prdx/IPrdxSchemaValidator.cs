using System.Text.Json.Nodes;
using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.DesignExchange.Prdx;

public enum PrdxSchemaKind
{
    Manifest,
    Project
}

public interface IPrdxSchemaValidator
{
    IReadOnlyList<Diagnostic> Validate(PrdxSchemaKind kind, JsonNode node);
}

public static class PrdxVersionPolicy
{
    public const string SupportedFormatVersion = "0.1.0";
    public const string SupportedSchemaVersion = "0.1.0";

    public static IReadOnlyList<string> SupportedFeatureFlags { get; } = [];

    public static IReadOnlyList<Diagnostic> ValidateManifest(JsonObject manifest)
    {
        var diagnostics = new List<Diagnostic>();
        var version = manifest["formatVersion"]?.GetValue<string>();
        if (!StringComparer.Ordinal.Equals(version, SupportedFormatVersion))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.VersionUnsupported,
                DiagnosticSeverity.Error,
                "Version",
                $"Unsupported PRDX formatVersion '{version}'. Supported version is '{SupportedFormatVersion}'.",
                Blocking: true));
        }

        foreach (var feature in manifest["featureFlags"] as JsonArray ?? [])
        {
            var value = feature?.GetValue<string>() ?? string.Empty;
            if (!SupportedFeatureFlags.Contains(value, StringComparer.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.FeatureUnsupported,
                    DiagnosticSeverity.Error,
                    "Version",
                    $"Unsupported PRDX feature flag '{value}'.",
                    Blocking: true));
            }
        }

        return diagnostics;
    }

    public static IReadOnlyList<Diagnostic> ValidateProject(JsonObject project)
    {
        var version = project["schemaVersion"]?.GetValue<string>();
        return StringComparer.Ordinal.Equals(version, SupportedSchemaVersion)
            ? []
            :
            [
                new Diagnostic(
                    DiagnosticCodes.VersionUnsupported,
                    DiagnosticSeverity.Error,
                    "Version",
                    $"Unsupported PRDX schemaVersion '{version}'. Supported version is '{SupportedSchemaVersion}'.",
                    Blocking: true)
            ];
    }
}
