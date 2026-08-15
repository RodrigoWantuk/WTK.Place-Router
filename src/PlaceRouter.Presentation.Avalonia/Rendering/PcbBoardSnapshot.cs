using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Geometry;

namespace PlaceRouter.Presentation.Rendering;

public enum PcbShapeKind
{
    Board,
    Cutout,
    Component,
    Pad,
    Track,
    Via,
    CopperZone,
    Keepout,
    Finding
}

public sealed record PcbShapeSnapshot(
    PcbShapeKind Kind,
    string EntityType,
    string EntityId,
    GeometryPolygon Geometry,
    string? LayerId,
    string? NetId,
    string Label,
    string Status);

public sealed record PcbRatsnestEdge(string NetId, GeometryPoint From, GeometryPoint To);

public sealed record PcbBoardSnapshot(
    GeometryEnvelope Bounds,
    IReadOnlyList<PcbShapeSnapshot> Shapes,
    IReadOnlyList<PcbRatsnestEdge> Ratsnest,
    IReadOnlyList<EntityReference> Selected,
    IReadOnlyList<PhysicalFinding> Findings)
{
    public static PcbBoardSnapshot Empty { get; } = new(GeometryEnvelope.Empty, [], [], [], []);

    public PcbShapeSnapshot? HitTest(GeometryPoint point)
    {
        return Shapes
            .Where(shape => shape.Geometry.Envelope.Contains(point))
            .OrderByDescending(static shape => Priority(shape.Kind))
            .FirstOrDefault();
    }

    private static int Priority(PcbShapeKind kind) =>
        kind switch
        {
            PcbShapeKind.Pad => 90,
            PcbShapeKind.Via => 85,
            PcbShapeKind.Track => 80,
            PcbShapeKind.Component => 70,
            PcbShapeKind.CopperZone => 50,
            PcbShapeKind.Keepout => 40,
            _ => 10
        };
}
