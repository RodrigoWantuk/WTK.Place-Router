namespace PlaceRouter.Core.Primitives;

public readonly record struct LengthUnits(long Value)
{
    public const long UnitsPerMillimeter = 1_000;

    public static LengthUnits FromMicrometers(long micrometers) => new(micrometers);

    public static LengthUnits FromMillimeters(decimal millimeters) =>
        new((long)decimal.Round(millimeters * UnitsPerMillimeter, 0, MidpointRounding.AwayFromZero));

    public override string ToString() => $"{Value} um";
}

public readonly record struct AngleDegrees(decimal Value)
{
    public static AngleDegrees Zero => new(0);

    public override string ToString() => FormattableString.Invariant($"{Value} deg");
}

public enum KnowledgeStatus
{
    Known,
    Inferred,
    Unknown,
    NotApplicable
}

public enum ProvenanceKind
{
    Imported,
    UserDefined,
    AiInferred,
    DeterministicInference,
    DeterministicMeasurement,
    Derived,
    ManufacturingProfile,
    Default,
    Unknown
}
