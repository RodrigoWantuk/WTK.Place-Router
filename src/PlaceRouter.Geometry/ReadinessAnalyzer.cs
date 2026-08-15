using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public sealed record ReadinessIssue(
    string Field,
    string ConsumerStage,
    string Impact,
    bool Blocking,
    bool FallbackAvailable,
    IReadOnlyList<EntityReference> EntityRefs);

public sealed record ReadinessReport(
    ReadinessStatus Status,
    IReadOnlyList<ReadinessIssue> Issues,
    int GeometryObjectsIndexed);

public sealed class ReadinessAnalyzer
{
    public ReadinessReport Analyze(CanonicalProject project, PhysicalGeometryModel geometry)
    {
        var issues = new List<ReadinessIssue>();
        foreach (var component in project.LogicalDesign.Components)
        {
            if (component.FootprintId is null)
            {
                issues.Add(new ReadinessIssue(
                    $"component[{component.Id.Value}].footprintId",
                    "Geometry/ConstraintEvaluation",
                    "Component physical geometry cannot be derived.",
                    Blocking: true,
                    FallbackAvailable: false,
                    [new EntityReference("COMPONENT", component.Id.Value)]));
                continue;
            }

            if (!project.LogicalDesign.Footprints.Any(f => f.Id == component.FootprintId.Value))
            {
                issues.Add(new ReadinessIssue(
                    $"footprint[{component.FootprintId.Value}]",
                    "Geometry/ConstraintEvaluation",
                    "Referenced footprint is not available.",
                    Blocking: true,
                    FallbackAvailable: false,
                    [new EntityReference("COMPONENT", component.Id.Value), new EntityReference("FOOTPRINT", component.FootprintId.Value.Value)]));
            }
        }

        foreach (var route in project.PhysicalDesignState.Routes)
        {
            foreach (var track in route.TrackSegments.Where(t => t.Width.Value <= 0))
            {
                issues.Add(new ReadinessIssue(
                    $"track[{track.Id.Value}].widthUnits",
                    "Manufacturing/ConstraintEvaluation",
                    "Track width is needed to apply minimum track width.",
                    Blocking: true,
                    FallbackAvailable: false,
                    [new EntityReference("TRACK_SEGMENT", track.Id.Value)]));
            }
        }

        foreach (var constraint in project.Constraints.Where(c => c.Enabled))
        {
            if (NeedsGeometry(constraint.Type) && !SelectorHasAnyTarget(project, constraint.Source))
            {
                issues.Add(new ReadinessIssue(
                    $"constraint[{constraint.Id.Value}].sourceSelector",
                    "ConstraintEvaluation",
                    "Selector has no concrete target for a geometry-dependent rule.",
                    Blocking: string.Equals(constraint.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase),
                    FallbackAvailable: false,
                    [new EntityReference("CONSTRAINT", constraint.Id.Value)]));
            }

            if (constraint.Target is not null && NeedsGeometry(constraint.Type) && !SelectorHasAnyTarget(project, constraint.Target))
            {
                issues.Add(new ReadinessIssue(
                    $"constraint[{constraint.Id.Value}].targetSelector",
                    "ConstraintEvaluation",
                    "Target selector has no concrete target for a geometry-dependent rule.",
                    Blocking: string.Equals(constraint.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase),
                    FallbackAvailable: false,
                    [new EntityReference("CONSTRAINT", constraint.Id.Value)]));
            }
        }

        var status = issues.Any(i => i.Blocking)
            ? ReadinessStatus.Blocked
            : issues.Count == 0 ? ReadinessStatus.Ready : ReadinessStatus.ReadyWithWarnings;

        return new ReadinessReport(status, issues, geometry.AllObjects.Count());
    }

    private static bool NeedsGeometry(string type) =>
        Canon(type) is "BOARDBOUNDS" or "COMPONENTOVERLAP" or "COURTYARD" or "KEEPOUT" or "MINIMUMSEPARATION" or "INSIDEREGION" or "OUTSIDEREGION" or "COPPERTOEDGE";

    private static bool SelectorHasAnyTarget(CanonicalProject project, ConstraintSelector selector) =>
        new ConstraintSelectorResolver(project).Resolve(selector).Any();

    private static string Canon(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
