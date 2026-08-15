using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Quadtree;
using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Geometry;

public sealed record SpatialQueryResult<T>(
    IReadOnlyList<T> Candidates,
    int BroadPhaseCandidates,
    int ExactMatches);

public interface ISpatialIndex<T>
{
    int Count { get; }

    void Insert(string id, GeometryEnvelope envelope, T item, LayerId? layerId = null);

    bool Remove(string id);

    void Update(string id, GeometryEnvelope envelope, T item, LayerId? layerId = null);

    IReadOnlyList<T> Query(GeometryEnvelope envelope, LayerId? layerId = null);
}

public sealed class QuadtreeSpatialIndex<T> : ISpatialIndex<T>
{
    private readonly Quadtree<Entry> _tree = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public void Insert(string id, GeometryEnvelope envelope, T item, LayerId? layerId = null)
    {
        if (!envelope.IsValid)
        {
            throw new ArgumentException("Envelope must be valid.", nameof(envelope));
        }

        var entry = new Entry(id, envelope, item, layerId);
        _entries.Add(id, entry);
        _tree.Insert(ToEnvelope(envelope), entry);
    }

    public bool Remove(string id)
    {
        if (!_entries.Remove(id, out var entry))
        {
            return false;
        }

        return _tree.Remove(ToEnvelope(entry.Envelope), entry);
    }

    public void Update(string id, GeometryEnvelope envelope, T item, LayerId? layerId = null)
    {
        Remove(id);
        Insert(id, envelope, item, layerId);
    }

    public IReadOnlyList<T> Query(GeometryEnvelope envelope, LayerId? layerId = null) =>
        _tree.Query(ToEnvelope(envelope))
            .Where(entry => entry.Envelope.Intersects(envelope))
            .Where(entry => layerId is null || entry.LayerId is null || entry.LayerId == layerId)
            .Select(entry => entry.Item)
            .ToArray();

    private static Envelope ToEnvelope(GeometryEnvelope envelope) =>
        new(envelope.MinX, envelope.MaxX, envelope.MinY, envelope.MaxY);

    private sealed record Entry(string Id, GeometryEnvelope Envelope, T Item, LayerId? LayerId);
}

public sealed class PhysicalObjectIndex
{
    private readonly IGeometryKernel _kernel;
    private readonly QuadtreeSpatialIndex<PhysicalObject> _index = new();

    public PhysicalObjectIndex(IGeometryKernel kernel)
    {
        _kernel = kernel;
    }

    public int Count => _index.Count;

    public static PhysicalObjectIndex Build(IGeometryKernel kernel, IEnumerable<PhysicalObject> objects)
    {
        var index = new PhysicalObjectIndex(kernel);
        foreach (var obj in objects)
        {
            index.Insert(obj);
        }

        return index;
    }

    public void Insert(PhysicalObject obj) =>
        _index.Insert(obj.Id, obj.Envelope, obj, obj.LayerId);

    public void Update(PhysicalObject obj) =>
        _index.Update(obj.Id, obj.Envelope, obj, obj.LayerId);

    public bool Remove(string id) => _index.Remove(id);

    public SpatialQueryResult<PhysicalObject> QueryExact(GeometryPolygon polygon, LayerId? layerId = null)
    {
        var candidates = _index.Query(polygon.Envelope, layerId);
        var exact = candidates.Where(candidate => _kernel.Intersects(candidate.Geometry, polygon) || _kernel.Distance(candidate.Geometry, polygon).Value == 0).ToArray();
        return new SpatialQueryResult<PhysicalObject>(exact, candidates.Count, exact.Length);
    }
}
