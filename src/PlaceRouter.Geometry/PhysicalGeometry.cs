using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public enum PhysicalObjectKind
{
    ComponentBody,
    ComponentCourtyard,
    Pad,
    Track,
    Via,
    CopperZone,
    Board,
    Region,
    Keepout
}

public sealed record PhysicalObject(
    string Id,
    string EntityType,
    string EntityId,
    PhysicalObjectKind Kind,
    GeometryPolygon Geometry,
    LayerId? LayerId,
    NetId? NetId)
{
    public GeometryEnvelope Envelope => Geometry.Envelope;
}

public sealed record PadGeometry(Pad Pad, IReadOnlyList<PhysicalObject> Objects);

public sealed record ComponentGeometry(
    Component Component,
    ComponentPose Pose,
    PhysicalObject? Body,
    PhysicalObject? Courtyard,
    IReadOnlyList<PadGeometry> Pads)
{
    public IEnumerable<PhysicalObject> Objects
    {
        get
        {
            if (Body is not null)
            {
                yield return Body;
            }

            if (Courtyard is not null)
            {
                yield return Courtyard;
            }

            foreach (var pad in Pads.SelectMany(p => p.Objects))
            {
                yield return pad;
            }
        }
    }

    public GeometryPolygon? PlacementBoundary => Courtyard?.Geometry ?? Body?.Geometry;
}

public sealed record PhysicalGeometryModel(
    IReadOnlyList<ComponentGeometry> Components,
    IReadOnlyList<PhysicalObject> BoardObjects,
    IReadOnlyList<PhysicalObject> RegionObjects,
    IReadOnlyList<PhysicalObject> KeepoutObjects,
    IReadOnlyList<PhysicalObject> RouteObjects,
    IReadOnlyList<PhysicalObject> ViaObjects,
    IReadOnlyList<PhysicalObject> CopperZoneObjects)
{
    public IEnumerable<PhysicalObject> AllObjects =>
        BoardObjects
            .Concat(RegionObjects)
            .Concat(KeepoutObjects)
            .Concat(Components.SelectMany(c => c.Objects))
            .Concat(RouteObjects)
            .Concat(ViaObjects)
            .Concat(CopperZoneObjects);

    public ComponentGeometry? Component(ComponentId id) =>
        Components.FirstOrDefault(c => c.Component.Id == id);
}

public sealed class PhysicalGeometryBuilder
{
    private readonly IGeometryKernel _kernel;

    public PhysicalGeometryBuilder(IGeometryKernel kernel)
    {
        _kernel = kernel;
    }

    public PhysicalGeometryModel Build(CanonicalProject project)
    {
        var components = project.PhysicalDesignState.ComponentPoses
            .Select(pose => BuildComponent(project, pose))
            .Where(c => c is not null)
            .Cast<ComponentGeometry>()
            .ToArray();

        return new PhysicalGeometryModel(
            components,
            BuildBoard(project).ToArray(),
            project.Board.Regions.Select(r => new PhysicalObject(r.Id.Value, "REGION", r.Id.Value, PhysicalObjectKind.Region, GeometryPolygon.From(r.Geometry), null, null)).ToArray(),
            project.Board.Keepouts.Select(k => new PhysicalObject(k.Id.Value, "KEEPOUT", k.Id.Value, PhysicalObjectKind.Keepout, GeometryPolygon.From(k.Geometry), null, null)).ToArray(),
            BuildRoutes(project).ToArray(),
            BuildVias(project).ToArray(),
            BuildCopperZones(project).ToArray());
    }

    private ComponentGeometry? BuildComponent(CanonicalProject project, ComponentPose pose)
    {
        var component = project.LogicalDesign.Components.FirstOrDefault(c => c.Id == pose.ComponentId);
        if (component?.FootprintId is null)
        {
            return null;
        }

        var footprint = project.LogicalDesign.Footprints.FirstOrDefault(f => f.Id == component.FootprintId.Value);
        if (footprint is null)
        {
            return null;
        }

        var transform = new GeometryTransform(GeometryPoint.From(pose.Position), pose.Rotation, pose.Side);
        var body = footprint.Body is null
            ? null
            : new PhysicalObject($"{component.Id.Value}:body", "COMPONENT", component.Id.Value, PhysicalObjectKind.ComponentBody, _kernel.TransformPolygon(GeometryPolygon.From(footprint.Body), transform), null, null);
        var courtyard = footprint.Courtyard is null
            ? null
            : new PhysicalObject($"{component.Id.Value}:courtyard", "COMPONENT", component.Id.Value, PhysicalObjectKind.ComponentCourtyard, _kernel.TransformPolygon(GeometryPolygon.From(footprint.Courtyard), transform), null, null);

        var pads = footprint.Pads.Select(pad => BuildPad(component, pose, pad)).ToArray();
        return new ComponentGeometry(component, pose, body, courtyard, pads);
    }

    private PadGeometry BuildPad(Component component, ComponentPose pose, Pad pad)
    {
        var localPad = pad.CustomPolygon is not null
            ? GeometryPolygon.From(pad.CustomPolygon)
            : PadPolygon(pad);
        var padTransform = new GeometryTransform(GeometryPoint.From(pad.Position), pad.Rotation, "TOP");
        var componentTransform = new GeometryTransform(GeometryPoint.From(pose.Position), pose.Rotation, pose.Side);
        var inFootprint = _kernel.TransformPolygon(localPad, padTransform);
        var absolute = _kernel.TransformPolygon(inFootprint, componentTransform);
        var objects = pad.LayerIds.Select(layerId =>
            new PhysicalObject($"{component.Id.Value}:pad:{pad.Id.Value}:{layerId.Value}", "PAD", pad.Id.Value, PhysicalObjectKind.Pad, absolute, layerId, null)).ToArray();
        return new PadGeometry(pad, objects);
    }

    private static GeometryPolygon PadPolygon(Pad pad)
    {
        var halfX = Math.Max(1, (pad.SizeX?.Value ?? 1) / 2);
        var halfY = Math.Max(1, (pad.SizeY?.Value ?? 1) / 2);
        return new GeometryPolygon(
            [
                new GeometryPoint(-halfX, -halfY),
                new GeometryPoint(halfX, -halfY),
                new GeometryPoint(halfX, halfY),
                new GeometryPoint(-halfX, halfY)
            ],
            []);
    }

    private static IEnumerable<PhysicalObject> BuildBoard(CanonicalProject project)
    {
        if (project.Board.Outline is not null)
        {
            yield return new PhysicalObject("board:outline", "BOARD", "BOARD", PhysicalObjectKind.Board, GeometryPolygon.From(project.Board.Outline), null, null);
        }
    }

    private static IEnumerable<PhysicalObject> BuildRoutes(CanonicalProject project)
    {
        foreach (var route in project.PhysicalDesignState.Routes)
        {
            foreach (var track in route.TrackSegments)
            {
                yield return new PhysicalObject(
                    track.Id.Value,
                    "TRACK_SEGMENT",
                    track.Id.Value,
                    PhysicalObjectKind.Track,
                    TrackPolygon(track),
                    track.LayerId,
                    route.NetId);
            }
        }
    }

    private static GeometryPolygon TrackPolygon(TrackSegment track)
    {
        var half = Math.Max(1, track.Width.Value / 2);
        if (track.Start.Y.Value == track.End.Y.Value)
        {
            var minX = Math.Min(track.Start.X.Value, track.End.X.Value);
            var maxX = Math.Max(track.Start.X.Value, track.End.X.Value);
            return Rect(minX, track.Start.Y.Value - half, maxX, track.Start.Y.Value + half);
        }

        if (track.Start.X.Value == track.End.X.Value)
        {
            var minY = Math.Min(track.Start.Y.Value, track.End.Y.Value);
            var maxY = Math.Max(track.Start.Y.Value, track.End.Y.Value);
            return Rect(track.Start.X.Value - half, minY, track.Start.X.Value + half, maxY);
        }

        var envelope = GeometryEnvelope.FromPoints([GeometryPoint.From(track.Start), GeometryPoint.From(track.End)]).Inflate(half);
        return Rect(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }

    private static IEnumerable<PhysicalObject> BuildVias(CanonicalProject project)
    {
        foreach (var via in project.PhysicalDesignState.Vias)
        {
            var radius = Math.Max(1, via.OuterDiameter.Value / 2);
            yield return new PhysicalObject(via.Id.Value, "VIA", via.Id.Value, PhysicalObjectKind.Via, Rect(via.Position.X.Value - radius, via.Position.Y.Value - radius, via.Position.X.Value + radius, via.Position.Y.Value + radius), null, via.NetId);
        }
    }

    private static IEnumerable<PhysicalObject> BuildCopperZones(CanonicalProject project)
    {
        foreach (var zone in project.PhysicalDesignState.CopperZones)
        {
            var index = 0;
            foreach (var polygon in zone.Geometry)
            {
                yield return new PhysicalObject($"{zone.Id.Value}:{index++}", "COPPER_ZONE", zone.Id.Value, PhysicalObjectKind.CopperZone, GeometryPolygon.From(polygon), zone.LayerId, zone.NetId);
            }
        }
    }

    private static GeometryPolygon Rect(long minX, long minY, long maxX, long maxY) =>
        new(
            [
                new GeometryPoint(minX, minY),
                new GeometryPoint(maxX, minY),
                new GeometryPoint(maxX, maxY),
                new GeometryPoint(minX, maxY)
            ],
            []);
}
