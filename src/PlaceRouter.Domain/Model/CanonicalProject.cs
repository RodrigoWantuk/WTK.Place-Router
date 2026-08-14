using System.Text.Json;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;

namespace PlaceRouter.Domain.Model;

public sealed record CanonicalProject(
    string SchemaVersion,
    ProjectId ProjectId,
    long ProjectRevision,
    ProjectMetadata Metadata,
    IReadOnlyList<SourceImport> SourceImports,
    LogicalDesign LogicalDesign,
    BoardDefinition Board,
    ManufacturingProfile ManufacturingProfile,
    IReadOnlyList<ConstraintDefinition> Constraints,
    Semantics Semantics,
    PhysicalDesignState PhysicalDesignState,
    IReadOnlyList<ReviewDecision> ReviewDecisions,
    ProjectSettings ProjectSettings,
    JsonElement Extensions)
{
    public const string CurrentSchemaVersion = "0.1.0";

    public ProjectSummary Summary => ProjectSummary.From(this);
}

public sealed record ProjectSummary(
    string ProjectId,
    long ProjectRevision,
    string Name,
    int Components,
    int Footprints,
    int Nets,
    int Layers,
    int ComponentPoses,
    int Routes,
    int Vias)
{
    public static ProjectSummary From(CanonicalProject project) =>
        new(
            project.ProjectId.Value,
            project.ProjectRevision,
            project.Metadata.Name,
            project.LogicalDesign.Components.Count,
            project.LogicalDesign.Footprints.Count,
            project.LogicalDesign.Nets.Count,
            project.Board.Layers.Count,
            project.PhysicalDesignState.ComponentPoses.Count,
            project.PhysicalDesignState.Routes.Count,
            project.PhysicalDesignState.Vias.Count);
}

public sealed record ProjectMetadata(
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    string? Author,
    IReadOnlyList<string> Tags);

public sealed record SourceImport(
    SourceImportId Id,
    string AdapterId,
    string AdapterVersion,
    string SourceType,
    string SourceName,
    string SourceSha256,
    DateTimeOffset ImportedAt,
    string? EmbeddedPath,
    IReadOnlyDictionary<string, string> Capabilities,
    IReadOnlyList<Diagnostic> LossDiagnostics);

public sealed record LogicalDesign(
    IReadOnlyList<Component> Components,
    IReadOnlyList<Footprint> Footprints,
    IReadOnlyList<Net> Nets,
    IReadOnlyList<NetClass> NetClasses,
    IReadOnlyList<Group> Groups);

public sealed record Component(
    ComponentId Id,
    string ReferenceDesignator,
    string? Value,
    string? PartNumber,
    string? Manufacturer,
    FootprintId? FootprintId,
    string PlacementPolicy,
    IReadOnlyDictionary<string, SourcedValue> Properties,
    SourcedValue? SemanticRole,
    JsonElement SourceMetadata,
    Provenance Provenance);

public sealed record Footprint(
    FootprintId Id,
    string Name,
    Point2 Origin,
    Polygon2? Body,
    Polygon2? Courtyard,
    LengthUnits? Height,
    IReadOnlyList<Pad> Pads,
    IReadOnlyList<FootprintHole> Holes,
    IReadOnlyList<FootprintGraphic> Graphics,
    Provenance? Provenance);

public sealed record Pad(
    PadId Id,
    string Number,
    string? Name,
    string? ConnectedPin,
    Point2 Position,
    AngleDegrees Rotation,
    string Shape,
    LengthUnits? SizeX,
    LengthUnits? SizeY,
    Polygon2? CustomPolygon,
    string PadType,
    IReadOnlyList<LayerId> LayerIds,
    LengthUnits? DrillDiameter,
    LengthUnits? MaskExpansion,
    LengthUnits? PasteExpansion);

public sealed record FootprintHole(Point2 Position, LengthUnits Diameter, bool Plated);

public sealed record FootprintGraphic(string Purpose, LayerId LayerId, JsonElement Geometry);

public sealed record Net(
    NetId Id,
    string Name,
    IReadOnlyList<NetEndpoint> Endpoints,
    NetClassId? NetClassId,
    IReadOnlyDictionary<string, SourcedValue> ElectricalProperties,
    IReadOnlyDictionary<string, SourcedValue> RoutingProperties,
    Provenance Provenance);

public sealed record NetEndpoint(ComponentId ComponentId, PadId? PadId, string? PinRef);

public sealed record NetClass(NetClassId Id, string Name, IReadOnlyDictionary<string, SourcedValue> Properties);

public sealed record Group(
    GroupId Id,
    string Name,
    string GroupType,
    GroupId? ParentGroupId,
    IReadOnlyList<GroupMember> Members,
    IReadOnlyDictionary<string, SourcedValue> Properties);

public sealed record GroupMember(string EntityType, string EntityId);

public sealed record BoardDefinition(
    Point2 Origin,
    Polygon2? Outline,
    IReadOnlyList<Polygon2> Cutouts,
    IReadOnlyList<BoardHole> Holes,
    LengthUnits? Thickness,
    SourcedValue? Material,
    IReadOnlyList<BoardLayer> Layers,
    IReadOnlyList<StackupEntry> Stackup,
    IReadOnlyList<Region> Regions,
    IReadOnlyList<Keepout> Keepouts);

public sealed record BoardHole(StableId Id, Point2 Position, LengthUnits Diameter, bool Plated, bool Locked);

public sealed record BoardLayer(
    LayerId Id,
    string Name,
    string LayerType,
    int Order,
    LengthUnits? Thickness,
    SourcedValue? Material,
    IReadOnlyDictionary<string, SourcedValue> Properties)
{
    public bool IsCopperCapable =>
        LayerType is "COPPER_SIGNAL" or "COPPER_PLANE";
}

public sealed record StackupEntry(LayerId LayerId, IReadOnlyList<LayerId> ReferenceLayerIds);

public sealed record Region(
    RegionId Id,
    string Name,
    Polygon2 Geometry,
    IReadOnlyList<LayerId> LayerIds,
    string? RegionType,
    IReadOnlyDictionary<string, SourcedValue> Properties);

public sealed record Keepout(
    KeepoutId Id,
    string Name,
    Polygon2 Geometry,
    IReadOnlyList<LayerId> LayerIds,
    IReadOnlyList<string> AppliesTo,
    string? Reason);

public sealed record ManufacturingProfile(
    StableId Id,
    string Name,
    string ProfileVersion,
    string? TemplateSource,
    DateTimeOffset? LastValidatedAt,
    IReadOnlyDictionary<string, SourcedValue> Capabilities,
    Provenance Provenance);

public sealed record ConstraintDefinition(
    ConstraintId Id,
    string Type,
    ConstraintSelector Source,
    ConstraintSelector? Target,
    JsonElement Parameters,
    string Enforcement,
    ConstraintScope Scope,
    Provenance Provenance,
    string? Reason,
    bool Enabled);

public sealed record ConstraintSelector(
    string Kind,
    string? EntityType,
    IReadOnlyList<string> EntityIds,
    string? Query);

public sealed record ConstraintScope(
    IReadOnlyList<LayerId> LayerIds,
    string? Measurement,
    string? ProjectionMode,
    IReadOnlyList<string> GeometryTypes);

public sealed record Semantics(IReadOnlyList<SemanticRelationship> Relationships);

public sealed record SemanticRelationship(
    SemanticRelationshipId Id,
    string Type,
    IReadOnlyList<SemanticEntityRef> EntityRefs,
    IReadOnlyDictionary<string, SourcedValue> Properties,
    double? Confidence,
    IReadOnlyList<string> EvidenceRefs,
    Provenance Provenance);

public sealed record SemanticEntityRef(string Role, string EntityType, string EntityId);

public sealed record PhysicalDesignState(
    PhysicalStateId StateId,
    long StateRevision,
    string Status,
    long BasedOnProjectRevision,
    IReadOnlyList<ComponentPose> ComponentPoses,
    IReadOnlyList<Route> Routes,
    IReadOnlyList<Via> Vias,
    IReadOnlyList<CopperZone> CopperZones,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy);

public sealed record ComponentPose(
    ComponentId ComponentId,
    Point2 Position,
    AngleDegrees Rotation,
    string Side,
    string PlacementState,
    string? LastModifiedBy);

public sealed record Route(
    RouteId Id,
    NetId NetId,
    string Status,
    string Policy,
    IReadOnlyList<TrackSegment> TrackSegments,
    IReadOnlyList<ViaId> ViaIds,
    Provenance Provenance,
    JsonElement Metadata);

public sealed record TrackSegment(
    TrackSegmentId Id,
    string GeometryKind,
    LayerId LayerId,
    LengthUnits Width,
    Point2 Start,
    Point2 End,
    Point2? ArcCenter,
    bool? Clockwise);

public sealed record Via(
    ViaId Id,
    NetId NetId,
    Point2 Position,
    LayerId StartLayerId,
    LayerId EndLayerId,
    string ViaType,
    LengthUnits DrillDiameter,
    LengthUnits OuterDiameter,
    string Policy,
    JsonElement PadstackMetadata);

public sealed record CopperZone(
    CopperZoneId Id,
    NetId? NetId,
    LayerId LayerId,
    IReadOnlyList<Polygon2> Geometry,
    LengthUnits? Clearance,
    int Priority,
    string Policy,
    JsonElement FillSettings);

public sealed record ReviewDecision(
    ReviewDecisionId Id,
    string DecisionType,
    string Fingerprint,
    IReadOnlyList<string> EntityRefs,
    string? Reason,
    DateTimeOffset CreatedAt,
    string CreatedBy);

public sealed record ProjectSettings(
    JsonElement OptimizationIntent,
    IReadOnlyList<ExportProfile> ExportProfiles,
    string? SourceEmbeddingPolicy);

public sealed record ExportProfile(StableId Id, string Name, string ProfileType, JsonElement Settings);

public sealed record Provenance(
    string Kind,
    string? SourceRef,
    string? Model,
    string? Operation,
    DateTimeOffset? Timestamp,
    string? Note)
{
    public static Provenance Unknown { get; } = new("UNKNOWN", null, null, null, null, null);
    public static Provenance UserDefined { get; } = new("USER_DEFINED", null, null, null, null, null);
}

public sealed record SourcedValue(
    JsonElement Value,
    string? Unit,
    string Status,
    double? Confidence,
    Provenance Provenance)
{
    public static SourcedValue Unknown() => new(JsonDefaults.NullElement, null, "UNKNOWN", null, Provenance.Unknown);
}

public static class JsonDefaults
{
    public static JsonElement NullElement { get; } = JsonDocument.Parse("null").RootElement.Clone();
    public static JsonElement EmptyObject { get; } = JsonDocument.Parse("{}").RootElement.Clone();
}
