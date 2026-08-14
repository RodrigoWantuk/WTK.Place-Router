using System.Reflection;
using System.Text.Json.Nodes;
using NJsonSchema;
using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class SchemaRegistry : IPrdxSchemaValidator
{
    private readonly Lazy<JsonSchema> _manifestSchema;
    private readonly Lazy<JsonSchema> _projectSchema;

    public SchemaRegistry()
    {
        _manifestSchema = new Lazy<JsonSchema>(() => LoadSchema("PlaceRouter.DesignExchange.Schemas.prdx-manifest.schema.json"));
        _projectSchema = new Lazy<JsonSchema>(() => LoadSchema("PlaceRouter.DesignExchange.Schemas.prdx-project.schema.json"));
    }

    public IReadOnlyList<Diagnostic> Validate(PrdxSchemaKind kind, JsonNode node) =>
        kind == PrdxSchemaKind.Manifest ? ValidateManifest(node) : ValidateProject(node);

    public IReadOnlyList<Diagnostic> ValidateManifest(JsonNode node) =>
        Validate(_manifestSchema.Value, node, DiagnosticCodes.ManifestSchema, "PRDX manifest schema validation failed.")
            .Concat(ValidateManifestConst(node))
            .ToArray();

    public IReadOnlyList<Diagnostic> ValidateProject(JsonNode node) =>
        Validate(_projectSchema.Value, node, DiagnosticCodes.ProjectSchema, "PRDX project schema validation failed.")
            .Concat(ValidateProjectConst(node))
            .ToArray();

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

    private static IReadOnlyList<Diagnostic> ValidateManifestConst(JsonNode node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        var diagnostics = new List<Diagnostic>();
        Check("format", "WTK.PRDX");
        Check("formatVersion", PrdxVersionPolicy.SupportedFormatVersion);
        Check("canonicalPayload", PrdxManifest.ProjectPayloadPath);
        return diagnostics;

        void Check(string propertyName, string expected)
        {
            var actual = obj[propertyName]?.GetValue<string>();
            if (!StringComparer.Ordinal.Equals(actual, expected))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ManifestSchema,
                    DiagnosticSeverity.Error,
                    "Schema",
                    $"PRDX manifest const validation failed for '{propertyName}'.",
                    Blocking: true));
            }
        }
    }

    private static IReadOnlyList<Diagnostic> ValidateProjectConst(JsonNode node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        var actual = obj["schemaVersion"]?.GetValue<string>();
        return StringComparer.Ordinal.Equals(actual, PrdxVersionPolicy.SupportedSchemaVersion)
            ? []
            :
            [
                new Diagnostic(
                    DiagnosticCodes.ProjectSchema,
                    DiagnosticSeverity.Error,
                    "Schema",
                    "PRDX project const validation failed for 'schemaVersion'.",
                    Blocking: true)
            ];
    }

    private static string NormalizeDraft202012ForNJsonSchema(string schemaText)
    {
        var root = JsonNode.Parse(schemaText) ?? throw new InvalidOperationException("Schema JSON was empty.");
        NormalizeNode(root);
        return root.ToJsonString();
    }

    private static void NormalizeNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.Remove("$defs", out var defs))
            {
                obj["definitions"] = defs;
            }

            if (obj["$ref"] is JsonValue refValue &&
                refValue.TryGetValue<string>(out var reference) &&
                reference.StartsWith("#/$defs/", StringComparison.Ordinal))
            {
                obj["$ref"] = "#/definitions/" + reference["#/$defs/".Length..];
            }

            foreach (var child in obj.ToArray())
            {
                if (child.Value is not null)
                {
                    NormalizeNode(child.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.OfType<JsonNode>())
            {
                NormalizeNode(child);
            }
        }
    }
}
