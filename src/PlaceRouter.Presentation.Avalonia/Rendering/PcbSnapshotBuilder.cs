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
        return new PcbBoardSnapshot(bounds, shapes, Ratsnest(project, geometry), selectedItems, findings);
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
            kind == PcbShapeKind.Pad ? obj.Id : obj.EntityId,
            obj.Geometry,
            obj.LayerId?.Value,
            obj.NetId?.Value,
            label,
            status);

    private static IReadOnlyList<PcbRatsnestEdge> Ratsnest(CanonicalProject project, PhysicalGeometryModel geometry)
    {
        var componentGeometry = geometry.Components.ToDictionary(static c => c.Component.Id.Value, StringComparer.Ordinal);
        var result = new List<PcbRatsnestEdge>();

        foreach (var net in project.LogicalDesign.Nets)
        {
            var points = new List<GeometryPoint>();
            foreach (var endpoint in net.Endpoints)
            {
                if (!componentGeometry.TryGetValue(endpoint.ComponentId.Value, out var component))
                {
                    continue;
                }

                var padGeometry = endpoint.PadId is null
                    ? null
                    : component.Pads.FirstOrDefault(p => p.Pad.Id == endpoint.PadId.Value);
                var objectGeometry = padGeometry?.Objects.FirstOrDefault()?.Geometry ?? component.PlacementBoundary;
                if (objectGeometry is null)
                {
                    continue;
                }

                var envelope = objectGeometry.Envelope;
                points.Add(new GeometryPoint((envelope.MinX + envelope.MaxX) / 2, (envelope.MinY + envelope.MaxY) / 2));
            }

            for (var i = 1; i < points.Count; i++)
            {
                result.Add(new PcbRatsnestEdge(net.Id.Value, points[i - 1], points[i]));
            }
        }

        return result;
    }
}
