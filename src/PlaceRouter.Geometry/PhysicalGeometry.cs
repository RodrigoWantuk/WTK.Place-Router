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
    NetId? NetId,
    IReadOnlySet<string>? AppliesTo = null,
    IReadOnlySet<LayerId>? LayerSpan = null,
    IReadOnlySet<RouteId>? RouteIds = null)
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
        var netByPad = NetByPad(project);
        var layerMirror = LayerMirror(project.Board.Layers);
        var components = project.PhysicalDesignState.ComponentPoses
            .Select(pose => BuildComponent(project, pose, netByPad, layerMirror))
            .Where(c => c is not null)
            .Cast<ComponentGeometry>()
            .ToArray();

        var routeIdsByVia = RouteIdsByVia(project);
        return new PhysicalGeometryModel(
            components,
            BuildBoard(project).ToArray(),
            project.Board.Regions.Select(r => new PhysicalObject(r.Id.Value, "REGION", r.Id.Value, PhysicalObjectKind.Region, GeometryPolygon.From(r.Geometry), null, null)).ToArray(),
            BuildKeepouts(project).ToArray(),
            BuildRoutes(project).ToArray(),
            BuildVias(project, routeIdsByVia).ToArray(),
            BuildCopperZones(project).ToArray());
    }

    private ComponentGeometry? BuildComponent(CanonicalProject project, ComponentPose pose, IReadOnlyDictionary<(string ComponentId, string PadId), NetId> netByPad, IReadOnlyDictionary<LayerId, LayerId> layerMirror)
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

        var pads = footprint.Pads.Select(pad => BuildPad(component, pose, pad, netByPad, layerMirror)).ToArray();
        return new ComponentGeometry(component, pose, body, courtyard, pads);
    }

    private PadGeometry BuildPad(Component component, ComponentPose pose, Pad pad, IReadOnlyDictionary<(string ComponentId, string PadId), NetId> netByPad, IReadOnlyDictionary<LayerId, LayerId> layerMirror)
    {
        var localPad = pad.CustomPolygon is not null
            ? GeometryPolygon.From(pad.CustomPolygon)
            : PadPolygon(pad);
        var padTransform = new GeometryTransform(GeometryPoint.From(pad.Position), pad.Rotation, "TOP");
        var componentTransform = new GeometryTransform(GeometryPoint.From(pose.Position), pose.Rotation, pose.Side);
        var inFootprint = _kernel.TransformPolygon(localPad, padTransform);
        var absolute = _kernel.TransformPolygon(inFootprint, componentTransform);
        netByPad.TryGetValue((component.Id.Value, pad.Id.Value), out var netId);
        var objects = pad.LayerIds.Select(layerId =>
        {
            var physicalLayer = MirrorLayer(layerId, pose.Side, layerMirror);
            return new PhysicalObject($"{component.Id.Value}:pad:{pad.Id.Value}:{physicalLayer.Value}", "PAD", pad.Id.Value, PhysicalObjectKind.Pad, absolute, physicalLayer, netId);
        }).ToArray();
        return new PadGeometry(pad, objects);
    }

    private static GeometryPolygon PadPolygon(Pad pad)
    {
        var halfX = Math.Max(1, (pad.SizeX?.Value ?? 1) / 2);
        var halfY = Math.Max(1, (pad.SizeY?.Value ?? 1) / 2);
        return Canon(pad.Shape) switch
        {
            "CIRCLE" => Ellipse(halfX, halfX),
            "OVAL" when halfX >= halfY => Capsule(new GeometryPoint(-(halfX - halfY), 0), new GeometryPoint(halfX - halfY, 0), halfY),
            "OVAL" => Capsule(new GeometryPoint(0, -(halfY - halfX)), new GeometryPoint(0, halfY - halfX), halfX),
            "ROUNDRECT" => RoundedRect(halfX, halfY, Math.Max(1, Math.Min(halfX, halfY) / 4)),
            "POLYGON" or "CUSTOM" when pad.CustomPolygon is not null => GeometryPolygon.From(pad.CustomPolygon),
            _ => Rect(-halfX, -halfY, halfX, halfY)
        };
    }

    private static IEnumerable<PhysicalObject> BuildBoard(CanonicalProject project)
    {
        if (project.Board.Outline is not null)
        {
            var holes = project.Board.Outline.Holes.Concat(project.Board.Cutouts.Select(c => c.Outer)).ToArray();
            yield return new PhysicalObject("board:outline", "BOARD", "BOARD", PhysicalObjectKind.Board, new GeometryPolygon(project.Board.Outline.Outer.Select(GeometryPoint.From).ToArray(), holes.Select(h => (IReadOnlyList<GeometryPoint>)h.Select(GeometryPoint.From).ToArray()).ToArray()), null, null);
        }
    }

    private static IEnumerable<PhysicalObject> BuildKeepouts(CanonicalProject project)
    {
        foreach (var keepout in project.Board.Keepouts)
        {
            var appliesTo = keepout.AppliesTo.Count == 0
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ALL" }
                : keepout.AppliesTo.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keepout.LayerIds.Count == 0)
            {
                yield return new PhysicalObject(keepout.Id.Value, "KEEPOUT", keepout.Id.Value, PhysicalObjectKind.Keepout, GeometryPolygon.From(keepout.Geometry), null, null, appliesTo);
                continue;
            }

            foreach (var layerId in keepout.LayerIds)
            {
                yield return new PhysicalObject($"{keepout.Id.Value}:{layerId.Value}", "KEEPOUT", keepout.Id.Value, PhysicalObjectKind.Keepout, GeometryPolygon.From(keepout.Geometry), layerId, null, appliesTo);
            }
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
                    route.NetId,
                    RouteIds: new HashSet<RouteId> { route.Id });
            }
        }
    }

    private static GeometryPolygon TrackPolygon(TrackSegment track)
    {
        var half = Math.Max(1, track.Width.Value / 2);
        if (Canon(track.GeometryKind) == "ARC" && track.ArcCenter is not null)
        {
            return ArcStrokePolygon(track, half);
        }

        return Capsule(GeometryPoint.From(track.Start), GeometryPoint.From(track.End), half);
    }

    private static IEnumerable<PhysicalObject> BuildVias(CanonicalProject project, IReadOnlyDictionary<ViaId, IReadOnlySet<RouteId>> routeIdsByVia)
    {
        foreach (var via in project.PhysicalDesignState.Vias)
        {
            var radius = Math.Max(1, via.OuterDiameter.Value / 2);
            routeIdsByVia.TryGetValue(via.Id, out var routeIds);
            yield return new PhysicalObject(via.Id.Value, "VIA", via.Id.Value, PhysicalObjectKind.Via, Circle(GeometryPoint.From(via.Position), radius), null, via.NetId, null, ViaLayerSpan(via, project.Board.Layers), routeIds);
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

    private static GeometryPolygon Ellipse(long radiusX, long radiusY, int segments = 32)
    {
        var points = Enumerable.Range(0, segments)
            .Select(i =>
            {
                var angle = 2d * Math.PI * i / segments;
                return new GeometryPoint((long)Math.Round(Math.Cos(angle) * radiusX), (long)Math.Round(Math.Sin(angle) * radiusY));
            })
            .ToArray();
        return new GeometryPolygon(points, []);
    }

    private static GeometryPolygon Circle(GeometryPoint center, long radius, int segments = 32)
    {
        var local = Ellipse(radius, radius, segments);
        return new GeometryPolygon(local.Outer.Select(p => new GeometryPoint(p.X + center.X, p.Y + center.Y)).ToArray(), []);
    }

    private static GeometryPolygon Capsule(GeometryPoint start, GeometryPoint end, long radius, int capSegments = 12)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt((double)dx * dx + (double)dy * dy);
        if (length == 0)
        {
            return Circle(start, radius);
        }

        var nx = -dy / length;
        var ny = dx / length;
        var normalAngle = Math.Atan2(ny, nx);
        var oppositeNormalAngle = Math.Atan2(-ny, -nx);
        var points = new List<GeometryPoint>();
        points.AddRange(ClockwiseArcPoints(end, normalAngle, oppositeNormalAngle, radius, capSegments));
        points.AddRange(ClockwiseArcPoints(start, oppositeNormalAngle, normalAngle, radius, capSegments));
        return new GeometryPolygon(points, []);
    }

    private static GeometryPolygon RoundedRect(long halfX, long halfY, long radius, int cornerSegments = 4)
    {
        radius = Math.Min(radius, Math.Min(halfX, halfY));
        var points = new List<GeometryPoint>();
        points.AddRange(ArcPoints(new GeometryPoint(halfX - radius, halfY - radius), 0, Math.PI / 2, radius, cornerSegments));
        points.AddRange(ArcPoints(new GeometryPoint(-halfX + radius, halfY - radius), Math.PI / 2, Math.PI, radius, cornerSegments));
        points.AddRange(ArcPoints(new GeometryPoint(-halfX + radius, -halfY + radius), Math.PI, Math.PI * 1.5, radius, cornerSegments));
        points.AddRange(ArcPoints(new GeometryPoint(halfX - radius, -halfY + radius), Math.PI * 1.5, Math.PI * 2, radius, cornerSegments));
        return new GeometryPolygon(points, []);
    }

    private static GeometryPolygon ArcStrokePolygon(TrackSegment track, long halfWidth)
    {
        var center = GeometryPoint.From(track.ArcCenter!.Value);
        var start = GeometryPoint.From(track.Start);
        var end = GeometryPoint.From(track.End);
        var radius = Math.Sqrt(Math.Pow(start.X - center.X, 2) + Math.Pow(start.Y - center.Y, 2));
        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var clockwise = track.Clockwise ?? false;
        var sweep = Sweep(startAngle, endAngle, clockwise);
        var steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 18)));
        var outer = radius + halfWidth;
        var inner = Math.Max(1, radius - halfWidth);
        var points = new List<GeometryPoint>();
        for (var i = 0; i <= steps; i++)
        {
            var angle = startAngle + sweep * i / steps;
            points.Add(new GeometryPoint(center.X + (long)Math.Round(Math.Cos(angle) * outer), center.Y + (long)Math.Round(Math.Sin(angle) * outer)));
        }

        for (var i = steps; i >= 0; i--)
        {
            var angle = startAngle + sweep * i / steps;
            points.Add(new GeometryPoint(center.X + (long)Math.Round(Math.Cos(angle) * inner), center.Y + (long)Math.Round(Math.Sin(angle) * inner)));
        }

        return new GeometryPolygon(points, []);
    }

    private static IEnumerable<GeometryPoint> ArcPoints(GeometryPoint center, double from, double to, long radius, int steps)
    {
        for (var i = 0; i <= steps; i++)
        {
            var angle = from + (to - from) * i / steps;
            yield return new GeometryPoint(center.X + (long)Math.Round(Math.Cos(angle) * radius), center.Y + (long)Math.Round(Math.Sin(angle) * radius));
        }
    }

    private static IEnumerable<GeometryPoint> ClockwiseArcPoints(GeometryPoint center, double from, double to, long radius, int steps)
    {
        var sweep = to - from;
        if (sweep > 0)
        {
            sweep -= Math.PI * 2;
        }

        for (var i = 0; i <= steps; i++)
        {
            var angle = from + sweep * i / steps;
            yield return new GeometryPoint(center.X + (long)Math.Round(Math.Cos(angle) * radius), center.Y + (long)Math.Round(Math.Sin(angle) * radius));
        }
    }

    private static double Sweep(double start, double end, bool clockwise)
    {
        var sweep = end - start;
        if (clockwise && sweep > 0)
        {
            sweep -= Math.PI * 2;
        }
        else if (!clockwise && sweep < 0)
        {
            sweep += Math.PI * 2;
        }

        return sweep;
    }

    private static IReadOnlyDictionary<(string ComponentId, string PadId), NetId> NetByPad(CanonicalProject project) =>
        project.LogicalDesign.Nets
            .SelectMany(net => net.Endpoints.Where(e => e.PadId is not null).Select(e => (e.ComponentId.Value, e.PadId!.Value.Value, net.Id)))
            .ToDictionary(e => (e.Item1, e.Item2), e => e.Id);

    private static IReadOnlyDictionary<ViaId, IReadOnlySet<RouteId>> RouteIdsByVia(CanonicalProject project)
    {
        var map = new Dictionary<ViaId, HashSet<RouteId>>();
        foreach (var route in project.PhysicalDesignState.Routes)
        {
            foreach (var viaId in route.ViaIds)
            {
                if (!map.TryGetValue(viaId, out var routeIds))
                {
                    routeIds = [];
                    map[viaId] = routeIds;
                }

                routeIds.Add(route.Id);
            }
        }

        return map.ToDictionary(k => k.Key, v => (IReadOnlySet<RouteId>)v.Value);
    }

    private static IReadOnlySet<LayerId> ViaLayerSpan(Via via, IReadOnlyList<BoardLayer> layers)
    {
        var start = layers.FirstOrDefault(l => l.Id == via.StartLayerId);
        var end = layers.FirstOrDefault(l => l.Id == via.EndLayerId);
        if (start is null || end is null)
        {
            return new HashSet<LayerId> { via.StartLayerId, via.EndLayerId };
        }

        var minOrder = Math.Min(start.Order, end.Order);
        var maxOrder = Math.Max(start.Order, end.Order);
        return layers
            .Where(l => l.IsCopperCapable && l.Order >= minOrder && l.Order <= maxOrder)
            .Select(l => l.Id)
            .ToHashSet();
    }

    private static IReadOnlyDictionary<LayerId, LayerId> LayerMirror(IReadOnlyList<BoardLayer> layers)
    {
        var map = new Dictionary<LayerId, LayerId>();
        PairByName("top", "bottom");
        PairByOrder(layers.Where(l => l.IsCopperCapable).OrderBy(l => l.Order).ToArray());
        return map;

        void PairByName(string top, string bottom)
        {
            var topLayers = layers.Where(l => l.Name.Contains(top, StringComparison.OrdinalIgnoreCase) || l.Id.Value.Contains(top, StringComparison.OrdinalIgnoreCase)).ToArray();
            var bottomLayers = layers.Where(l => l.Name.Contains(bottom, StringComparison.OrdinalIgnoreCase) || l.Id.Value.Contains(bottom, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var t in topLayers)
            {
                var suffix = t.Id.Value.Replace(top, string.Empty, StringComparison.OrdinalIgnoreCase).Replace("TOP", string.Empty, StringComparison.OrdinalIgnoreCase);
                var match = bottomLayers.FirstOrDefault(b => b.Id.Value.Replace(bottom, string.Empty, StringComparison.OrdinalIgnoreCase).Replace("BOTTOM", string.Empty, StringComparison.OrdinalIgnoreCase).Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    ?? bottomLayers.FirstOrDefault(b => string.Equals(b.LayerType, t.LayerType, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    map[t.Id] = match.Id;
                    map[match.Id] = t.Id;
                }
            }
        }

        void PairByOrder(IReadOnlyList<BoardLayer> ordered)
        {
            if (ordered.Count >= 2)
            {
                map.TryAdd(ordered[0].Id, ordered[^1].Id);
                map.TryAdd(ordered[^1].Id, ordered[0].Id);
            }
        }
    }

    private static LayerId MirrorLayer(LayerId layerId, string side, IReadOnlyDictionary<LayerId, LayerId> layerMirror) =>
        string.Equals(side, "BOTTOM", StringComparison.OrdinalIgnoreCase) && layerMirror.TryGetValue(layerId, out var mirrored)
            ? mirrored
            : layerId;

    private static string Canon(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
