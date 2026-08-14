using System.Reflection;
using System.Text.Json.Nodes;
using NJsonSchema;
using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class SchemaRegistry
{
    private readonly Lazy<JsonSchema> _manifestSchema;
    private readonly Lazy<JsonSchema> _projectSchema;

    public SchemaRegistry()
    {
        _manifestSchema = new Lazy<JsonSchema>(() => LoadSchema("PlaceRouter.DesignExchange.Schemas.prdx-manifest.schema.json"));
        _projectSchema = new Lazy<JsonSchema>(() => LoadSchema("PlaceRouter.DesignExchange.Schemas.prdx-project.schema.json"));
    }

    public IReadOnlyList<Diagnostic> ValidateManifest(JsonNode node) =>
        Validate(_manifestSchema.Value, node, DiagnosticCodes.ManifestSchema, "PRDX manifest schema validation failed.");

    public IReadOnlyList<Diagnostic> ValidateProject(JsonNode node) =>
        Validate(_projectSchema.Value, node, DiagnosticCodes.ProjectSchema, "PRDX project schema validation failed.");

    private static IReadOnlyList<Diagnostic> Validate(JsonSchema schema, JsonNode node, string code, string message)
    {
        var errors = schema.Validate(node.ToJsonString());
        if (errors.Count == 0)
        {
            return [];
        }

        var details = errors
            .Select(e => $"{e.Path}: {e.Kind}")
            .Take(12)
            .ToArray();

        return
        [
            new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                "Schema",
                details.Length == 0 ? message : $"{message} {string.Join(" | ", details)}",
                Blocking: true)
        ];
    }

    private static JsonSchema LoadSchema(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        var schemaText = NormalizeDraft202012ForNJsonSchema(reader.ReadToEnd());
        return JsonSchema.FromJsonAsync(schemaText).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"Embedded schema resource '{resourceName}' could not be parsed.");
    }

    private static string NormalizeDraft202012ForNJsonSchema(string schemaText) =>
        schemaText
            .Replace("\"$defs\"", "\"definitions\"", StringComparison.Ordinal)
            .Replace("#/$defs/", "#/definitions/", StringComparison.Ordinal);
}
