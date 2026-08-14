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
    string PayloadSha256)
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
        ["featureFlags"] = new JsonArray(),
        ["sourceFingerprints"] = new JsonArray()
    };
}
