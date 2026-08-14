using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlaceRouter.Domain.Prdx;

public sealed class CanonicalProject
{
    public const string CurrentSchemaVersion = "0.1.0";

    public CanonicalProject(JsonObject root)
    {
        Root = root;
    }

    public JsonObject Root { get; }

    public string ProjectId => RequiredString("projectId");

    public long ProjectRevision
    {
        get
        {
            if (Root["projectRevision"] is not JsonValue value)
            {
                return 0;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            return value.TryGetValue<int>(out var intValue) ? intValue : 0;
        }
    }

    public string Name => Root["metadata"]?["name"]?.GetValue<string>() ?? ProjectId;

    public ProjectSummary Summary => ProjectSummary.From(this);

    public static CanonicalProject Parse(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("PRDX project payload root must be a JSON object.");

        return new CanonicalProject(node);
    }

    public string ToJson(bool indented = true)
    {
        return Root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = null
        });
    }

    public byte[] ToUtf8JsonBytes(bool indented = true) => Encoding.UTF8.GetBytes(ToJson(indented));

    public CanonicalProject DeepClone() => new((JsonObject)Root.DeepClone());

    private string RequiredString(string propertyName) =>
        Root[propertyName]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Project is missing required property '{propertyName}'.");
}

public sealed record ProjectSummary(
    string ProjectId,
    long ProjectRevision,
    string Name,
    int Components,
    int Footprints,
    int Nets,
    int Layers,
    int ComponentPoses,
    int Routes,
    int Vias)
{
    public static ProjectSummary From(CanonicalProject project)
    {
        var root = project.Root;
        return new ProjectSummary(
            project.ProjectId,
            project.ProjectRevision,
            project.Name,
            Count(root["logicalDesign"]?["components"]),
            Count(root["logicalDesign"]?["footprints"]),
            Count(root["logicalDesign"]?["netlist"]?["nets"]),
            Count(root["board"]?["layers"]),
            Count(root["physicalDesignState"]?["componentPoses"]),
            Count(root["physicalDesignState"]?["routes"]),
            Count(root["physicalDesignState"]?["vias"]));
    }

    private static int Count(JsonNode? node) => node is JsonArray array ? array.Count : 0;
}
