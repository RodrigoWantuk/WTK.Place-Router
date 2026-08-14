using System.Text.Json.Nodes;

namespace PlaceRouter.Domain.Prdx;

public static class CanonicalProjectFactory
{
    public static CanonicalProject CreateEmpty(string name, string? projectId = null, DateTimeOffset? now = null)
    {
        var timestamp = (now ?? DateTimeOffset.UtcNow).ToString("O");
        projectId ??= "prj_" + Guid.NewGuid().ToString("N");

        JsonObject unknownProvenance() => new()
        {
            ["kind"] = "UNKNOWN",
            ["sourceRef"] = null,
            ["model"] = null,
            ["operation"] = null,
            ["timestamp"] = null,
            ["note"] = null
        };

        JsonObject unknownValue() => new()
        {
            ["value"] = null,
            ["unit"] = null,
            ["status"] = "UNKNOWN",
            ["confidence"] = null,
            ["provenance"] = unknownProvenance()
        };

        var root = new JsonObject
        {
            ["schemaVersion"] = CanonicalProject.CurrentSchemaVersion,
            ["projectId"] = projectId,
            ["projectRevision"] = 0,
            ["metadata"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = null,
                ["createdAt"] = timestamp,
                ["modifiedAt"] = timestamp,
                ["author"] = null,
                ["tags"] = new JsonArray()
            },
            ["sourceImports"] = new JsonArray(),
            ["logicalDesign"] = new JsonObject
            {
                ["components"] = new JsonArray(),
                ["footprints"] = new JsonArray(),
                ["netlist"] = new JsonObject { ["nets"] = new JsonArray() },
                ["netClasses"] = new JsonArray(),
                ["groups"] = new JsonArray()
            },
            ["board"] = new JsonObject
            {
                ["origin"] = new JsonObject { ["x"] = 0, ["y"] = 0 },
                ["outline"] = new JsonObject
                {
                    ["outer"] = new JsonArray(
                        new JsonObject { ["x"] = 0, ["y"] = 0 },
                        new JsonObject { ["x"] = 10_000, ["y"] = 0 },
                        new JsonObject { ["x"] = 10_000, ["y"] = 10_000 },
                        new JsonObject { ["x"] = 0, ["y"] = 10_000 }),
                    ["holes"] = new JsonArray()
                },
                ["cutouts"] = new JsonArray(),
                ["holes"] = new JsonArray(),
                ["thicknessUnits"] = null,
                ["material"] = unknownValue(),
                ["layers"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "layer_top_cu",
                        ["name"] = "Top Copper",
                        ["layerType"] = "COPPER_SIGNAL",
                        ["order"] = 0,
                        ["thicknessUnits"] = null,
                        ["material"] = unknownValue(),
                        ["properties"] = new JsonObject()
                    },
                    new JsonObject
                    {
                        ["id"] = "layer_bottom_cu",
                        ["name"] = "Bottom Copper",
                        ["layerType"] = "COPPER_SIGNAL",
                        ["order"] = 1,
                        ["thicknessUnits"] = null,
                        ["material"] = unknownValue(),
                        ["properties"] = new JsonObject()
                    }),
                ["stackup"] = new JsonArray(
                    new JsonObject { ["layerId"] = "layer_top_cu", ["referenceLayerIds"] = new JsonArray() },
                    new JsonObject { ["layerId"] = "layer_bottom_cu", ["referenceLayerIds"] = new JsonArray() }),
                ["regions"] = new JsonArray(),
                ["keepouts"] = new JsonArray()
            },
            ["manufacturingProfile"] = new JsonObject
            {
                ["id"] = "mfg_default",
                ["name"] = "Default Unknown",
                ["profileVersion"] = "0",
                ["templateSource"] = null,
                ["lastValidatedAt"] = null,
                ["capabilities"] = new JsonObject(),
                ["provenance"] = unknownProvenance()
            },
            ["constraints"] = new JsonArray(),
            ["semantics"] = new JsonObject { ["relationships"] = new JsonArray() },
            ["physicalDesignState"] = new JsonObject
            {
                ["stateId"] = "state_001",
                ["stateRevision"] = 0,
                ["status"] = "UNROUTED",
                ["basedOnProjectRevision"] = 0,
                ["componentPoses"] = new JsonArray(),
                ["routes"] = new JsonArray(),
                ["vias"] = new JsonArray(),
                ["copperZones"] = new JsonArray(),
                ["lastModifiedAt"] = timestamp,
                ["lastModifiedBy"] = "PlaceRouter"
            },
            ["reviewDecisions"] = new JsonArray(),
            ["projectSettings"] = new JsonObject
            {
                ["optimizationIntent"] = new JsonObject { ["profile"] = "BALANCED" },
                ["exportProfiles"] = new JsonArray(),
                ["sourceEmbeddingPolicy"] = "ASK"
            },
            ["extensions"] = new JsonObject()
        };

        return new CanonicalProject(root);
    }
}
