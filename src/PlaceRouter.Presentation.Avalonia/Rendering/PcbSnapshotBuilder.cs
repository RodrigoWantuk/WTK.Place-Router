using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;
using PlaceRouter.Geometry;

namespace PlaceRouter.Presentation.Rendering;

public sealed class PcbSnapshotBuilder
{
    private readonly PhysicalGeometryBuilder _geometryBuilder;

    public PcbSnapshotBuilder(IGeometryKernel? kernel = null)
    {
        _geometryBuilder = new PhysicalGeometryBuilder(kernel ?? new ClipperGeometryKernel());
    }

    public PcbBoardSnapshot Build(
        CanonicalProject? project,
        ConstraintEvaluationReport? report,
        IReadOnlyList<EntityReference>? selected = null)
    {
        if (project is null)
        {
            return PcbBoardSnapshot.Empty;
        }

        var geometry = _geometryBuilder.Build(project);
        var shapes = new List<PcbShapeSnapshot>();
        shapes.AddRange(geometry.BoardObjects.Select(o => Shape(PcbShapeKind.Board, o, "Board", "normal")));
        shapes.AddRange(geometry.KeepoutObjects.Select(o => Shape(PcbShapeKind.Keepout, o, "Keepout", "warning")));
        shapes.AddRange(geometry.CopperZoneObjects.Select(o => Shape(PcbShapeKind.CopperZone, o, "Zone", "normal")));
        shapes.AddRange(geometry.RouteObjects.Select(o => Shape(PcbShapeKind.Track, o, "Track", "normal")));
        shapes.AddRange(geometry.ViaObjects.Select(o => Shape(PcbShapeKind.Via, o, "Via", "normal")));
        shapes.AddRange(geometry.Components.SelectMany(ComponentShapes));

        var findings = report?.Findings ?? [];
        var failingIds = findings
            .SelectMany(static f => f.AffectedEntities)
            .Select(static e => $"{e.EntityType}:{e.EntityId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        shapes = shapes
            .Select(shape => failingIds.Contains($"{shape.EntityType}:{shape.EntityId}") ? shape with { Status = "violation" } : shape)
            .ToList();

        var bounds = GeometryEnvelope.FromPoints(shapes.SelectMany(static s => s.Geometry.Outer));
        var selectedItems = selected ?? [];
        return new PcbBoardSnapshot(bounds, shapes, Ratsnest(project), selectedItems, findings);
    }

    private static IEnumerable<PcbShapeSnapshot> ComponentShapes(ComponentGeometry component)
    {
        if (component.Body is not null)
        {
            yield return Shape(PcbShapeKind.Component, component.Body, component.Component.ReferenceDesignator, "normal");
        }

        foreach (var pad in component.Pads.SelectMany(static p => p.Objects))
        {
            yield return Shape(PcbShapeKind.Pad, pad, "Pad", "normal");
        }
    }

    private static PcbShapeSnapshot Shape(PcbShapeKind kind, PhysicalObject obj, string label, string status) =>
        new(
            kind,
            obj.EntityType,
            obj.EntityId,
            obj.Geometry,
            obj.LayerId?.Value,
            obj.NetId?.Value,
            label,
            status);

    private static IReadOnlyList<PcbRatsnestEdge> Ratsnest(CanonicalProject project)
    {
        var poses = project.PhysicalDesignState.ComponentPoses.ToDictionary(static p => p.ComponentId.Value, StringComparer.Ordinal);
        var footprints = project.LogicalDesign.Footprints.ToDictionary(static f => f.Id.Value, StringComparer.Ordinal);
        var components = project.LogicalDesign.Components.ToDictionary(static c => c.Id.Value, StringComparer.Ordinal);
        var result = new List<PcbRatsnestEdge>();

        foreach (var net in project.LogicalDesign.Nets)
        {
            var points = new List<GeometryPoint>();
            foreach (var endpoint in net.Endpoints)
            {
                if (!components.TryGetValue(endpoint.ComponentId.Value, out var component) ||
                    component.FootprintId is null ||
                    !footprints.TryGetValue(component.FootprintId.Value.Value, out var footprint) ||
                    !poses.TryGetValue(endpoint.ComponentId.Value, out var pose))
                {
                    continue;
                }

                var pad = endpoint.PadId is null
                    ? null
                    : footprint.Pads.FirstOrDefault(p => p.Id == endpoint.PadId.Value);
                var x = pose.Position.X.Value + (pad?.Position.X.Value ?? 0);
                var y = pose.Position.Y.Value + (pad?.Position.Y.Value ?? 0);
                points.Add(new GeometryPoint(x, y));
            }

            for (var i = 1; i < points.Count; i++)
            {
                result.Add(new PcbRatsnestEdge(net.Id.Value, points[i - 1], points[i]));
            }
        }

        return result;
    }
}
