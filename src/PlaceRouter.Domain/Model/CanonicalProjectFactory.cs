using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Domain.Model;

public static class CanonicalProjectFactory
{
    public static CanonicalProject CreateIncomplete(string name, string? projectId = null, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new CanonicalProject(
            CanonicalProject.CurrentSchemaVersion,
            new ProjectId(projectId ?? "prj_" + Guid.NewGuid().ToString("N")),
            0,
            new ProjectMetadata(name, null, timestamp, timestamp, null, []),
            [],
            new LogicalDesign([], [], [], [], []),
            new BoardDefinition(
                new Point2(LengthUnits.FromMicrometers(0), LengthUnits.FromMicrometers(0)),
                null,
                [],
                [],
                null,
                SourcedValue.Unknown(),
                [],
                [],
                [],
                []),
            new ManufacturingProfile(new StableId("mfg_unknown"), "Unknown", "0", null, null, new Dictionary<string, SourcedValue>(StringComparer.Ordinal), Provenance.Unknown),
            [],
            new Semantics([]),
            new PhysicalDesignState(
                new PhysicalStateId("state_001"),
                0,
                "INCOMPLETE",
                0,
                [],
                [],
                [],
                [],
                timestamp,
                "PlaceRouter"),
            [],
            new ProjectSettings(JsonDefaults.EmptyObject, [], "ASK"),
            JsonDefaults.EmptyObject);
    }
}
