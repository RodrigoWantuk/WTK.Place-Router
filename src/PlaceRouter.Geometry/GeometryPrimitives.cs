using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Geometry;

public readonly record struct GeometryPoint(long X, long Y)
{
    public static GeometryPoint From(Point2 point) => new(point.X.Value, point.Y.Value);

    public Point2 ToPoint2() => new(new LengthUnits(X), new LengthUnits(Y));
}

public readonly record struct GeometryEnvelope(long MinX, long MinY, long MaxX, long MaxY)
{
    public bool IsValid => MinX <= MaxX && MinY <= MaxY;

    public long Width => MaxX - MinX;

    public long Height => MaxY - MinY;

    public static GeometryEnvelope Empty { get; } = new(0, 0, -1, -1);

    public static GeometryEnvelope FromPoints(IEnumerable<GeometryPoint> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return Empty;
        }

        var minX = enumerator.Current.X;
        var maxX = enumerator.Current.X;
        var minY = enumerator.Current.Y;
        var maxY = enumerator.Current.Y;

        while (enumerator.MoveNext())
        {
            minX = Math.Min(minX, enumerator.Current.X);
            maxX = Math.Max(maxX, enumerator.Current.X);
            minY = Math.Min(minY, enumerator.Current.Y);
            maxY = Math.Max(maxY, enumerator.Current.Y);
        }

        return new GeometryEnvelope(minX, minY, maxX, maxY);
    }

    public bool Intersects(GeometryEnvelope other) =>
        IsValid &&
        other.IsValid &&
        MinX <= other.MaxX &&
        MaxX >= other.MinX &&
        MinY <= other.MaxY &&
        MaxY >= other.MinY;

    public bool Contains(GeometryPoint point) =>
        IsValid &&
        point.X >= MinX &&
        point.X <= MaxX &&
        point.Y >= MinY &&
        point.Y <= MaxY;

    public GeometryEnvelope Inflate(long delta) =>
        IsValid
            ? new GeometryEnvelope(MinX - delta, MinY - delta, MaxX + delta, MaxY + delta)
            : this;
}

public sealed record GeometryPolygon(
    IReadOnlyList<GeometryPoint> Outer,
    IReadOnlyList<IReadOnlyList<GeometryPoint>> Holes)
{
    public static GeometryPolygon Empty { get; } = new([], []);

    public bool IsEmpty => Outer.Count < 3;

    public GeometryEnvelope Envelope => GeometryEnvelope.FromPoints(Outer.Concat(Holes.SelectMany(h => h)));

    public Polygon2 ToPolygon2() =>
        new(Outer.Select(p => p.ToPoint2()).ToArray(), Holes.Select(h => (IReadOnlyList<Point2>)h.Select(p => p.ToPoint2()).ToArray()).ToArray());

    public static GeometryPolygon From(Polygon2 polygon) =>
        new(
            polygon.Outer.Select(GeometryPoint.From).ToArray(),
            polygon.Holes.Select(h => (IReadOnlyList<GeometryPoint>)h.Select(GeometryPoint.From).ToArray()).ToArray());
}

public sealed record GeometrySegment(GeometryPoint Start, GeometryPoint End)
{
    public GeometryEnvelope Envelope => GeometryEnvelope.FromPoints([Start, End]);
}

public sealed record GeometryTransform(
    GeometryPoint Translation,
    AngleDegrees Rotation,
    string Side)
{
    public bool IsBottom => string.Equals(Side, "BOTTOM", StringComparison.OrdinalIgnoreCase);
}

public interface IGeometryKernel
{
    GeometryPoint TransformPoint(GeometryPoint point, GeometryTransform transform);

    GeometryPolygon TransformPolygon(GeometryPolygon polygon, GeometryTransform transform);

    GeometryEnvelope Bounds(GeometryPolygon polygon);

    bool Intersects(GeometryPolygon first, GeometryPolygon second);

    bool Contains(GeometryPolygon container, GeometryPoint point);

    bool Contains(GeometryPolygon container, GeometryPolygon candidate);

    LengthUnits Distance(GeometryPolygon first, GeometryPolygon second);

    IReadOnlyList<GeometryPolygon> Offset(GeometryPolygon polygon, LengthUnits delta);

    bool SegmentIntersectsPolygon(GeometrySegment segment, GeometryPolygon polygon);
}
