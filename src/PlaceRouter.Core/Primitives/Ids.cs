namespace PlaceRouter.Core.Primitives;

public readonly record struct StableId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProjectRevision(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct ProjectId(string Value) { public override string ToString() => Value; }
public readonly record struct ComponentId(string Value) { public override string ToString() => Value; }
public readonly record struct FootprintId(string Value) { public override string ToString() => Value; }
public readonly record struct PadId(string Value) { public override string ToString() => Value; }
public readonly record struct NetId(string Value) { public override string ToString() => Value; }
public readonly record struct NetClassId(string Value) { public override string ToString() => Value; }
public readonly record struct LayerId(string Value) { public override string ToString() => Value; }
public readonly record struct GroupId(string Value) { public override string ToString() => Value; }
public readonly record struct RegionId(string Value) { public override string ToString() => Value; }
public readonly record struct KeepoutId(string Value) { public override string ToString() => Value; }
public readonly record struct ConstraintId(string Value) { public override string ToString() => Value; }
public readonly record struct SemanticRelationshipId(string Value) { public override string ToString() => Value; }
public readonly record struct PhysicalStateId(string Value) { public override string ToString() => Value; }
public readonly record struct RouteId(string Value) { public override string ToString() => Value; }
public readonly record struct TrackSegmentId(string Value) { public override string ToString() => Value; }
public readonly record struct ViaId(string Value) { public override string ToString() => Value; }
public readonly record struct CopperZoneId(string Value) { public override string ToString() => Value; }
public readonly record struct SourceImportId(string Value) { public override string ToString() => Value; }
public readonly record struct ReviewDecisionId(string Value) { public override string ToString() => Value; }
