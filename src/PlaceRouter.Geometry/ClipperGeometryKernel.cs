using Clipper2Lib;
using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Geometry;

public sealed class ClipperGeometryKernel : IGeometryKernel
{
    public GeometryPoint TransformPoint(GeometryPoint point, GeometryTransform transform)
    {
        var localX = transform.IsBottom ? -point.X : point.X;
        var localY = point.Y;
        var rotated = Rotate(localX, localY, NormalizeDegrees(transform.Rotation.Value));
        return new GeometryPoint(rotated.X + transform.Translation.X, rotated.Y + transform.Translation.Y);
    }

    public GeometryPolygon TransformPolygon(GeometryPolygon polygon, GeometryTransform transform) =>
        polygon.IsEmpty
            ? GeometryPolygon.Empty
            : new GeometryPolygon(
                polygon.Outer.Select(p => TransformPoint(p, transform)).ToArray(),
                polygon.Holes.Select(h => (IReadOnlyList<GeometryPoint>)h.Select(p => TransformPoint(p, transform)).ToArray()).ToArray());

    public GeometryEnvelope Bounds(GeometryPolygon polygon) => polygon.Envelope;

    public bool Intersects(GeometryPolygon first, GeometryPolygon second)
    {
        if (first.IsEmpty || second.IsEmpty || !first.Envelope.Intersects(second.Envelope))
        {
            return false;
        }

        var solution = Clipper.Intersect(ToPaths(first), ToPaths(second), FillRule.NonZero);
        return solution.Any(path => Math.Abs(SignedArea(path)) > 0);
    }

    public bool Contains(GeometryPolygon container, GeometryPoint point)
    {
        if (container.IsEmpty || !container.Envelope.Contains(point))
        {
            return false;
        }

        var outer = PointInRing(point, container.Outer);
        if (!outer)
        {
            return false;
        }

        return container.Holes.All(hole => !PointInRing(point, hole));
    }

    public bool Contains(GeometryPolygon container, GeometryPolygon candidate) =>
        !container.IsEmpty &&
        !candidate.IsEmpty &&
        candidate.Outer.All(p => Contains(container, p)) &&
        container.Holes.All(hole => !hole.Any(p => PointInRing(p, candidate.Outer))) &&
        !IntersectsAnyEdge(container, candidate);

    public LengthUnits Distance(GeometryPolygon first, GeometryPolygon second)
    {
        if (first.IsEmpty || second.IsEmpty)
        {
            return new LengthUnits(long.MaxValue);
        }

        if (Intersects(first, second) || Edges(first).Any(a => Edges(second).Any(b => SegmentsTouch(a, b))) || first.Outer.Any(p => Contains(second, p)) || second.Outer.Any(p => Contains(first, p)))
        {
            return new LengthUnits(0);
        }

        var minSquared = double.PositiveInfinity;
        foreach (var firstEdge in Edges(first))
        {
            foreach (var secondEdge in Edges(second))
            {
                minSquared = Math.Min(minSquared, SegmentDistanceSquared(firstEdge, secondEdge));
            }
        }

        return new LengthUnits((long)Math.Ceiling(Math.Sqrt(minSquared)));
    }

    public IReadOnlyList<GeometryPolygon> Offset(GeometryPolygon polygon, LengthUnits delta)
    {
        if (polygon.IsEmpty)
        {
            return [];
        }

        var inflated = Clipper.InflatePaths(ToPaths(polygon), delta.Value, JoinType.Miter, EndType.Polygon);
        return FromPaths(inflated);
    }

    public bool SegmentIntersectsPolygon(GeometrySegment segment, GeometryPolygon polygon) =>
        !polygon.IsEmpty &&
        segment.Envelope.Intersects(polygon.Envelope) &&
        (Contains(polygon, segment.Start) || Contains(polygon, segment.End) || Edges(polygon).Any(edge => SegmentsTouch(edge, segment)));

    private static (long X, long Y) Rotate(long x, long y, decimal degrees)
    {
        if (degrees == 0)
        {
            return (x, y);
        }

        if (degrees == 90)
        {
            return (-y, x);
        }

        if (degrees == 180)
        {
            return (-x, -y);
        }

        if (degrees == 270)
        {
            return (y, -x);
        }

        var radians = (double)degrees * Math.PI / 180d;
        return ((long)Math.Round(x * Math.Cos(radians) - y * Math.Sin(radians), MidpointRounding.AwayFromZero),
            (long)Math.Round(x * Math.Sin(radians) + y * Math.Cos(radians), MidpointRounding.AwayFromZero));
    }

    private static decimal NormalizeDegrees(decimal value)
    {
        var result = value % 360;
        return result < 0 ? result + 360 : result;
    }

    private static Paths64 ToPaths(GeometryPolygon polygon)
    {
        var paths = new Paths64 { ToPath(polygon.Outer, positive: true) };
        paths.AddRange(polygon.Holes.Select(h => ToPath(h, positive: false)));
        return paths;
    }

    private static Path64 ToPath(IReadOnlyList<GeometryPoint> points, bool positive)
    {
        var oriented = points;
        var area = SignedArea(points);
        if ((positive && area < 0) || (!positive && area > 0))
        {
            oriented = points.Reverse().ToArray();
        }

        var path = new Path64(oriented.Count);
        foreach (var point in oriented)
        {
            path.Add(new Point64(point.X, point.Y));
        }

        return path;
    }

    private static IReadOnlyList<GeometryPolygon> FromPaths(Paths64 paths)
    {
        var outers = new List<Path64>();
        var holes = new List<Path64>();
        foreach (var path in paths.Where(p => p.Count >= 3 && Math.Abs(SignedArea(p)) > 0))
        {
            if (SignedArea(path) >= 0)
            {
                outers.Add(path);
            }
            else
            {
                holes.Add(path);
            }
        }

        return outers
            .Select(outer => new GeometryPolygon(
                outer.Select(p => new GeometryPoint(p.X, p.Y)).ToArray(),
                holes
                    .Where(hole => hole.Any(p => PointInRing(new GeometryPoint(p.X, p.Y), outer.Select(o => new GeometryPoint(o.X, o.Y)).ToArray())))
                    .Select(hole => (IReadOnlyList<GeometryPoint>)hole.Select(p => new GeometryPoint(p.X, p.Y)).ToArray())
                    .ToArray()))
            .Where(p => !p.IsEmpty)
            .ToArray();
    }

    private static long SignedArea(Path64 path)
    {
        long sum = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum;
    }

    private static long SignedArea(IReadOnlyList<GeometryPoint> path)
    {
        long sum = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum;
    }

    private static IEnumerable<GeometrySegment> Edges(GeometryPolygon polygon)
    {
        foreach (var edge in RingEdges(polygon.Outer))
        {
            yield return edge;
        }

        foreach (var hole in polygon.Holes)
        {
            foreach (var edge in RingEdges(hole))
            {
                yield return edge;
            }
        }
    }

    private static IEnumerable<GeometrySegment> RingEdges(IReadOnlyList<GeometryPoint> ring)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            yield return new GeometrySegment(ring[i], ring[(i + 1) % ring.Count]);
        }
    }

    private static bool PointInRing(GeometryPoint point, IReadOnlyList<GeometryPoint> ring)
    {
        var inside = false;
        var j = ring.Count - 1;
        for (var i = 0; i < ring.Count; j = i++)
        {
            if (PointOnSegment(point, new GeometrySegment(ring[j], ring[i])))
            {
                return true;
            }

            if (((ring[i].Y > point.Y) != (ring[j].Y > point.Y)) &&
                point.X < (double)(ring[j].X - ring[i].X) * (point.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool IntersectsAnyEdge(GeometryPolygon container, GeometryPolygon candidate) =>
        Edges(container).Any(a => Edges(candidate).Any(b => SegmentsTouch(a, b)));

    private static bool SegmentsTouch(GeometrySegment first, GeometrySegment second)
    {
        var d1 = Direction(second.Start, second.End, first.Start);
        var d2 = Direction(second.Start, second.End, first.End);
        var d3 = Direction(first.Start, first.End, second.Start);
        var d4 = Direction(first.Start, first.End, second.End);

        return (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) ||
               (d1 == 0 && PointOnSegment(first.Start, second)) ||
               (d2 == 0 && PointOnSegment(first.End, second)) ||
               (d3 == 0 && PointOnSegment(second.Start, first)) ||
               (d4 == 0 && PointOnSegment(second.End, first));
    }

    private static long Direction(GeometryPoint a, GeometryPoint b, GeometryPoint c) =>
        ((c.X - a.X) * (b.Y - a.Y)) - ((b.X - a.X) * (c.Y - a.Y));

    private static bool PointOnSegment(GeometryPoint point, GeometrySegment segment) =>
        Math.Min(segment.Start.X, segment.End.X) <= point.X &&
        point.X <= Math.Max(segment.Start.X, segment.End.X) &&
        Math.Min(segment.Start.Y, segment.End.Y) <= point.Y &&
        point.Y <= Math.Max(segment.Start.Y, segment.End.Y) &&
        Direction(segment.Start, segment.End, point) == 0;

    private static double SegmentDistanceSquared(GeometrySegment first, GeometrySegment second)
    {
        if (SegmentsTouch(first, second))
        {
            return 0;
        }

        return new[]
        {
            PointSegmentDistanceSquared(first.Start, second),
            PointSegmentDistanceSquared(first.End, second),
            PointSegmentDistanceSquared(second.Start, first),
            PointSegmentDistanceSquared(second.End, first)
        }.Min();
    }

    private static double PointSegmentDistanceSquared(GeometryPoint point, GeometrySegment segment)
    {
        var dx = (double)(segment.End.X - segment.Start.X);
        var dy = (double)(segment.End.Y - segment.Start.Y);
        if (dx == 0 && dy == 0)
        {
            return Squared(point.X - segment.Start.X, point.Y - segment.Start.Y);
        }

        var t = ((point.X - segment.Start.X) * dx + (point.Y - segment.Start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Max(0, Math.Min(1, t));
        return Squared(point.X - (segment.Start.X + t * dx), point.Y - (segment.Start.Y + t * dy));
    }

    private static double Squared(double x, double y) => x * x + y * y;
}
