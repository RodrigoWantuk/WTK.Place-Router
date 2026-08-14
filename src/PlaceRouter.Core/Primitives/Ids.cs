namespace PlaceRouter.Core.Primitives;

public readonly record struct StableId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProjectId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LayerId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProjectRevision(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
