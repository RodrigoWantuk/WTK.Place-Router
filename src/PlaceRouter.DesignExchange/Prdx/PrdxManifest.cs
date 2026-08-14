using System.Text.Json.Nodes;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed record PrdxManifest(
    string Format,
    string FormatVersion,
    string ProjectId,
    long ProjectRevision,
    string CreatedAt,
    string ModifiedAt,
    string? ApplicationVersion,
    string CanonicalPayload,
    string PayloadSha256,
    IReadOnlyList<string>? FeatureFlags = null,
    IReadOnlyList<ManifestSourceFingerprint>? SourceFingerprints = null)
{
    public const string ExpectedFormat = "WTK.PRDX";
    public const string CurrentFormatVersion = "0.1.0";
    public const string ProjectPayloadPath = "project.json";
    public const string ManifestPath = "manifest.json";

    public JsonObject ToJsonObject() => new()
    {
        ["format"] = Format,
        ["formatVersion"] = FormatVersion,
        ["projectId"] = ProjectId,
        ["projectRevision"] = ProjectRevision,
        ["createdAt"] = CreatedAt,
        ["modifiedAt"] = ModifiedAt,
        ["applicationVersion"] = ApplicationVersion,
        ["canonicalPayload"] = CanonicalPayload,
        ["payloadSha256"] = PayloadSha256,
        ["featureFlags"] = ToArray(FeatureFlags ?? []),
        ["sourceFingerprints"] = ToArray(SourceFingerprints ?? [], f => new JsonObject
        {
            ["sourceImportId"] = f.SourceImportId,
            ["sha256"] = f.Sha256
        })
    };

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray ToArray<T>(IEnumerable<T> values, Func<T, JsonNode?> map)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(map(value));
        }

        return array;
    }
}

public sealed record ManifestSourceFingerprint(string SourceImportId, string Sha256);
