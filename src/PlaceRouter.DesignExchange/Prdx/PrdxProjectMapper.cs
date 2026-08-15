using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.DesignExchange.Prdx;

internal static class PrdxProjectMapper
{
    public static CanonicalProject ToDomain(JsonObject root) =>
        new(
            S(root, "schemaVersion"),
            new ProjectId(S(root, "projectId")),
            L(root, "projectRevision"),
            Metadata(O(root, "metadata")),
            Arr(root, "sourceImports").Select(SourceImport).ToArray(),
            Logical(O(root, "logicalDesign")),
            Board(O(root, "board")),
            Manufacturing(O(root, "manufacturingProfile")),
            Arr(root, "constraints").Select(Constraint).ToArray(),
            Semantics(O(root, "semantics")),
            Physical(O(root, "physicalDesignState")),
            Arr(root, "reviewDecisions").Select(ReviewDecision).ToArray(),
            Settings(O(root, "projectSettings")),
            Element(root["extensions"]));

    public static JsonObject ToJson(CanonicalProject project) => new()
    {
        ["schemaVersion"] = project.SchemaVersion,
        ["projectId"] = project.ProjectId.Value,
        ["projectRevision"] = project.ProjectRevision,
        ["metadata"] = Metadata(project.Metadata),
        ["sourceImports"] = Array(project.SourceImports, SourceImport),
        ["logicalDesign"] = Logical(project.LogicalDesign),
        ["board"] = Board(project.Board),
        ["manufacturingProfile"] = Manufacturing(project.ManufacturingProfile),
        ["constraints"] = Array(project.Constraints, Constraint),
        ["semantics"] = Semantics(project.Semantics),
        ["physicalDesignState"] = Physical(project.PhysicalDesignState),
        ["reviewDecisions"] = Array(project.ReviewDecisions, ReviewDecision),
        ["projectSettings"] = Settings(project.ProjectSettings),
        ["extensions"] = Node(project.Extensions)
    };

    private static ProjectMetadata Metadata(JsonObject o) =>
        new(S(o, "name"), SN(o, "description"), D(o, "createdAt"), D(o, "modifiedAt"), SN(o, "author"), Strings(o["tags"]).ToArray());

    private static JsonObject Metadata(ProjectMetadata metadata) => new()
    {
        ["name"] = metadata.Name,
        ["description"] = metadata.Description,
        ["createdAt"] = metadata.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        ["modifiedAt"] = metadata.ModifiedAt.ToString("O", CultureInfo.InvariantCulture),
        ["author"] = metadata.Author,
        ["tags"] = Array(metadata.Tags, v => (JsonNode?)v)
    };

    private static SourceImport SourceImport(JsonObject o) =>
        new(
            new SourceImportId(S(o, "id")),
            S(o, "adapterId"),
            S(o, "adapterVersion"),
            S(o, "sourceType"),
            S(o, "sourceName"),
            S(o, "sourceSha256"),
            D(o, "importedAt"),
            SN(o, "embeddedPath"),
            Object(o["capabilities"]).ToDictionary(k => k.Key, v => v.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal),
            Arr(o, "lossDiagnostics").Select(DiagnosticValue).ToArray());

    private static JsonObject SourceImport(SourceImport sourceImport) => new()
    {
        ["id"] = sourceImport.Id.Value,
        ["adapterId"] = sourceImport.AdapterId,
        ["adapterVersion"] = sourceImport.AdapterVersion,
        ["sourceType"] = sourceImport.SourceType,
        ["sourceName"] = sourceImport.SourceName,
        ["sourceSha256"] = sourceImport.SourceSha256,
        ["importedAt"] = sourceImport.ImportedAt.ToString("O", CultureInfo.InvariantCulture),
        ["embeddedPath"] = sourceImport.EmbeddedPath,
        ["capabilities"] = Dictionary(sourceImport.Capabilities, v => v),
        ["lossDiagnostics"] = Array(sourceImport.LossDiagnostics, DiagnosticValue)
    };

    private static LogicalDesign Logical(JsonObject o) => new(
        Arr(o, "components").Select(Component).ToArray(),
        Arr(o, "footprints").Select(Footprint).ToArray(),
        Arr(O(o, "netlist"), "nets").Select(Net).ToArray(),
        Arr(o, "netClasses").Select(NetClass).ToArray(),
        Arr(o, "groups").Select(Group).ToArray());

    private static JsonObject Logical(LogicalDesign logical) => new()
    {
        ["components"] = Array(logical.Components, Component),
        ["footprints"] = Array(logical.Footprints, Footprint),
        ["netlist"] = new JsonObject { ["nets"] = Array(logical.Nets, Net) },
        ["netClasses"] = Array(logical.NetClasses, NetClass),
        ["groups"] = Array(logical.Groups, Group)
    };

    private static Component Component(JsonObject o) => new(
        new ComponentId(S(o, "id")),
        S(o, "referenceDesignator"),
        SN(o, "value"),
        SN(o, "partNumber"),
        SN(o, "manufacturer"),
        SN(o, "footprintId") is { } fp ? new FootprintId(fp) : null,
        S(o, "placementPolicy"),
        SourcedDictionary(o["properties"]),
        o["semanticRole"] is JsonObject semantic ? SourcedValue(semantic) : null,
        Element(o["sourceMetadata"]),
        Provenance(O(o, "provenance")));

    private static JsonObject Component(Component c) => new()
    {
        ["id"] = c.Id.Value,
        ["referenceDesignator"] = c.ReferenceDesignator,
        ["value"] = c.Value,
        ["partNumber"] = c.PartNumber,
        ["manufacturer"] = c.Manufacturer,
        ["footprintId"] = c.FootprintId?.Value,
        ["placementPolicy"] = c.PlacementPolicy,
        ["properties"] = Dictionary(c.Properties, SourcedValue),
        ["semanticRole"] = c.SemanticRole is null ? null : SourcedValue(c.SemanticRole),
        ["sourceMetadata"] = Node(c.SourceMetadata),
        ["provenance"] = Provenance(c.Provenance)
    };

    private static Footprint Footprint(JsonObject o) => new(
        new FootprintId(S(o, "id")),
        S(o, "name"),
        Point(O(o, "origin")),
        PolygonN(o["body"]),
        PolygonN(o["courtyard"]),
        UnitsN(o, "heightUnits"),
        Arr(o, "pads").Select(Pad).ToArray(),
        Arr(o, "holes").Select(FootprintHole).ToArray(),
        Arr(o, "graphics").Select(FootprintGraphic).ToArray(),
        o["provenance"] is JsonObject p ? Provenance(p) : null);

    private static JsonObject Footprint(Footprint f) => new()
    {
        ["id"] = f.Id.Value,
        ["name"] = f.Name,
        ["origin"] = Point(f.Origin),
        ["body"] = PolygonOrNull(f.Body),
        ["courtyard"] = PolygonOrNull(f.Courtyard),
        ["heightUnits"] = f.Height?.Value,
        ["pads"] = Array(f.Pads, Pad),
        ["holes"] = Array(f.Holes, FootprintHole),
        ["graphics"] = Array(f.Graphics, FootprintGraphic),
        ["provenance"] = f.Provenance is null ? null : Provenance(f.Provenance)
    };

    private static Pad Pad(JsonObject o) => new(
        new PadId(S(o, "id")),
        S(o, "number"),
        SN(o, "name"),
        SN(o, "connectedPin"),
        Point(O(o, "position")),
        new AngleDegrees(Dec(o, "rotationDeg")),
        S(o, "shape"),
        UnitsN(o, "sizeXUnits"),
        UnitsN(o, "sizeYUnits"),
        PolygonN(o["customPolygon"]),
        S(o, "padType"),
        Strings(o["layerIds"]).Select(v => new LayerId(v)).ToArray(),
        UnitsN(o, "drillDiameterUnits"),
        UnitsN(o, "maskExpansionUnits"),
        UnitsN(o, "pasteExpansionUnits"));

    private static JsonObject Pad(Pad p) => new()
    {
        ["id"] = p.Id.Value,
        ["number"] = p.Number,
        ["name"] = p.Name,
        ["connectedPin"] = p.ConnectedPin,
        ["position"] = Point(p.Position),
        ["rotationDeg"] = p.Rotation.Value,
        ["shape"] = p.Shape,
        ["sizeXUnits"] = p.SizeX?.Value,
        ["sizeYUnits"] = p.SizeY?.Value,
        ["customPolygon"] = PolygonOrNull(p.CustomPolygon),
        ["padType"] = p.PadType,
        ["layerIds"] = Array(p.LayerIds, v => (JsonNode?)v.Value),
        ["drillDiameterUnits"] = p.DrillDiameter?.Value,
        ["maskExpansionUnits"] = p.MaskExpansion?.Value,
        ["pasteExpansionUnits"] = p.PasteExpansion?.Value
    };

    private static FootprintHole FootprintHole(JsonObject o) =>
        new(Point(O(o, "position")), Units(o, "diameterUnits"), B(o, "plated"));

    private static JsonObject FootprintHole(FootprintHole h) => new()
    {
        ["position"] = Point(h.Position),
        ["diameterUnits"] = h.Diameter.Value,
        ["plated"] = h.Plated
    };

    private static FootprintGraphic FootprintGraphic(JsonObject o) =>
        new(S(o, "purpose"), new LayerId(S(o, "layerId")), Element(o["geometry"]));

    private static JsonObject FootprintGraphic(FootprintGraphic g) => new()
    {
        ["purpose"] = g.Purpose,
        ["layerId"] = g.LayerId.Value,
        ["geometry"] = Node(g.Geometry)
    };

    private static Net Net(JsonObject o) => new(
        new NetId(S(o, "id")),
        S(o, "name"),
        Arr(o, "endpoints").Select(Endpoint).ToArray(),
        SN(o, "netClassId") is { } nc ? new NetClassId(nc) : null,
        SourcedDictionary(o["electricalProperties"]),
        SourcedDictionary(o["routingProperties"]),
        Provenance(O(o, "provenance")));

    private static JsonObject Net(Net n) => new()
    {
        ["id"] = n.Id.Value,
        ["name"] = n.Name,
        ["endpoints"] = Array(n.Endpoints, Endpoint),
        ["netClassId"] = n.NetClassId?.Value,
        ["electricalProperties"] = Dictionary(n.ElectricalProperties, SourcedValue),
        ["routingProperties"] = Dictionary(n.RoutingProperties, SourcedValue),
        ["provenance"] = Provenance(n.Provenance)
    };

    private static NetEndpoint Endpoint(JsonObject o) =>
        new(new ComponentId(S(o, "componentId")), SN(o, "padId") is { } pad ? new PadId(pad) : null, SN(o, "pinRef"));

    private static JsonObject Endpoint(NetEndpoint e) => new()
    {
        ["componentId"] = e.ComponentId.Value,
        ["padId"] = e.PadId?.Value,
        ["pinRef"] = e.PinRef
    };

    private static NetClass NetClass(JsonObject o) =>
        new(new NetClassId(S(o, "id")), S(o, "name"), SourcedDictionary(o["properties"]));

    private static JsonObject NetClass(NetClass n) => new()
    {
        ["id"] = n.Id.Value,
        ["name"] = n.Name,
        ["properties"] = Dictionary(n.Properties, SourcedValue)
    };

    private static Group Group(JsonObject o) => new(
        new GroupId(S(o, "id")),
        S(o, "name"),
        S(o, "groupType"),
        SN(o, "parentGroupId") is { } parent ? new GroupId(parent) : null,
        Arr(o, "members").Select(m => new GroupMember(S(m, "entityType"), S(m, "entityId"))).ToArray(),
        SourcedDictionary(o["properties"]));

    private static JsonObject Group(Group g) => new()
    {
        ["id"] = g.Id.Value,
        ["name"] = g.Name,
        ["groupType"] = g.GroupType,
        ["parentGroupId"] = g.ParentGroupId?.Value,
        ["members"] = Array(g.Members, m => new JsonObject { ["entityType"] = m.EntityType, ["entityId"] = m.EntityId }),
        ["properties"] = Dictionary(g.Properties, SourcedValue)
    };

    private static BoardDefinition Board(JsonObject o) => new(
        Point(O(o, "origin")),
        PolygonN(o["outline"]),
        Arr(o, "cutouts").Select(Polygon).ToArray(),
        Arr(o, "holes").Select(BoardHole).ToArray(),
        UnitsN(o, "thicknessUnits"),
        o["material"] is JsonObject material ? SourcedValue(material) : null,
        Arr(o, "layers").Select(Layer).ToArray(),
        Arr(o, "stackup").Select(Stackup).ToArray(),
        Arr(o, "regions").Select(Region).ToArray(),
        Arr(o, "keepouts").Select(Keepout).ToArray());

    private static JsonObject Board(BoardDefinition b) => new()
    {
        ["origin"] = Point(b.Origin),
        ["outline"] = PolygonOrNull(b.Outline),
        ["cutouts"] = Array(b.Cutouts, Polygon),
        ["holes"] = Array(b.Holes, BoardHole),
        ["thicknessUnits"] = b.Thickness?.Value,
        ["material"] = b.Material is null ? null : SourcedValue(b.Material),
        ["layers"] = Array(b.Layers, Layer),
        ["stackup"] = Array(b.Stackup, Stackup),
        ["regions"] = Array(b.Regions, Region),
        ["keepouts"] = Array(b.Keepouts, Keepout)
    };

    private static BoardHole BoardHole(JsonObject o) =>
        new(new StableId(S(o, "id")), Point(O(o, "position")), Units(o, "diameterUnits"), B(o, "plated"), BN(o, "locked") ?? true);

    private static JsonObject BoardHole(BoardHole h) => new()
    {
        ["id"] = h.Id.Value,
        ["position"] = Point(h.Position),
        ["diameterUnits"] = h.Diameter.Value,
        ["plated"] = h.Plated,
        ["locked"] = h.Locked
    };

    private static BoardLayer Layer(JsonObject o) => new(
        new LayerId(S(o, "id")),
        S(o, "name"),
        S(o, "layerType"),
        (int)L(o, "order"),
        UnitsN(o, "thicknessUnits"),
        o["material"] is JsonObject material ? SourcedValue(material) : null,
        SourcedDictionary(o["properties"]));

    private static JsonObject Layer(BoardLayer l) => new()
    {
        ["id"] = l.Id.Value,
        ["name"] = l.Name,
        ["layerType"] = l.LayerType,
        ["order"] = l.Order,
        ["thicknessUnits"] = l.Thickness?.Value,
        ["material"] = l.Material is null ? null : SourcedValue(l.Material),
        ["properties"] = Dictionary(l.Properties, SourcedValue)
    };

    private static StackupEntry Stackup(JsonObject o) =>
        new(new LayerId(S(o, "layerId")), Strings(o["referenceLayerIds"]).Select(v => new LayerId(v)).ToArray());

    private static JsonObject Stackup(StackupEntry s) => new()
    {
        ["layerId"] = s.LayerId.Value,
        ["referenceLayerIds"] = Array(s.ReferenceLayerIds, v => (JsonNode?)v.Value)
    };

    private static Region Region(JsonObject o) => new(
        new RegionId(S(o, "id")),
        S(o, "name"),
        Polygon(O(o, "geometry")),
        Strings(o["layerIds"]).Select(v => new LayerId(v)).ToArray(),
        SN(o, "regionType"),
        SourcedDictionary(o["properties"]));

    private static JsonObject Region(Region r) => new()
    {
        ["id"] = r.Id.Value,
        ["name"] = r.Name,
        ["regionType"] = r.RegionType,
        ["geometry"] = Polygon(r.Geometry),
        ["layerIds"] = Array(r.LayerIds, v => (JsonNode?)v.Value),
        ["properties"] = Dictionary(r.Properties, SourcedValue)
    };

    private static Keepout Keepout(JsonObject o) => new(
        new KeepoutId(S(o, "id")),
        S(o, "id"),
        Polygon(O(o, "geometry")),
        Strings(o["layerIds"]).Select(v => new LayerId(v)).ToArray(),
        Strings(o["appliesTo"]).ToArray(),
        null);

    private static JsonObject Keepout(Keepout k) => new()
    {
        ["id"] = k.Id.Value,
        ["geometry"] = Polygon(k.Geometry),
        ["layerIds"] = Array(k.LayerIds, v => (JsonNode?)v.Value),
        ["appliesTo"] = Array(k.AppliesTo, v => (JsonNode?)v)
    };

    private static ManufacturingProfile Manufacturing(JsonObject o) => new(
        new StableId(S(o, "id")),
        S(o, "name"),
        S(o, "profileVersion"),
        SN(o, "templateSource"),
        DN(o, "lastValidatedAt"),
        SourcedDictionary(o["capabilities"]),
        Provenance(O(o, "provenance")));

    private static JsonObject Manufacturing(ManufacturingProfile m) => new()
    {
        ["id"] = m.Id.Value,
        ["name"] = m.Name,
        ["profileVersion"] = m.ProfileVersion,
        ["templateSource"] = m.TemplateSource,
        ["lastValidatedAt"] = m.LastValidatedAt?.ToString("O", CultureInfo.InvariantCulture),
        ["capabilities"] = Dictionary(m.Capabilities, SourcedValue),
        ["provenance"] = Provenance(m.Provenance)
    };

    private static ConstraintDefinition Constraint(JsonObject o) => new(
        new ConstraintId(S(o, "id")),
        S(o, "type"),
        Selector(O(o, "sourceSelector")),
        o["targetSelector"] is JsonObject target ? Selector(target) : null,
        Element(o["parameters"]),
        S(o, "enforcement"),
        Scope(O(o, "scope")),
        Provenance(O(o, "provenance")),
        SN(o, "reason"),
        B(o, "enabled"));

    private static JsonObject Constraint(ConstraintDefinition c) => new()
    {
        ["id"] = c.Id.Value,
        ["type"] = c.Type,
        ["sourceSelector"] = Selector(c.Source),
        ["targetSelector"] = c.Target is null ? null : Selector(c.Target),
        ["parameters"] = Node(c.Parameters),
        ["enforcement"] = c.Enforcement,
        ["scope"] = Scope(c.Scope),
        ["provenance"] = Provenance(c.Provenance),
        ["reason"] = c.Reason,
        ["enabled"] = c.Enabled,
        ["userMetadata"] = new JsonObject()
    };

    private static ConstraintSelector Selector(JsonObject o) =>
        new(S(o, "kind"), SN(o, "entityType"), Strings(o["entityIds"]).ToArray(), SN(o, "query"));

    private static JsonObject Selector(ConstraintSelector s) => new()
    {
        ["kind"] = s.Kind,
        ["entityType"] = s.EntityType,
        ["entityIds"] = Array(s.EntityIds, v => (JsonNode?)v),
        ["query"] = s.Query
    };

    private static ConstraintScope Scope(JsonObject o) =>
        new(Strings(o["layerIds"]).Select(v => new LayerId(v)).ToArray(), SN(o, "measurement"), SN(o, "projectionMode"), Strings(o["geometryTypes"]).ToArray());

    private static JsonObject Scope(ConstraintScope s) => new()
    {
        ["layerIds"] = Array(s.LayerIds, v => (JsonNode?)v.Value),
        ["measurement"] = s.Measurement,
        ["projectionMode"] = s.ProjectionMode,
        ["geometryTypes"] = Array(s.GeometryTypes, v => (JsonNode?)v)
    };

    private static Semantics Semantics(JsonObject o) => new(Arr(o, "relationships").Select(Relationship).ToArray());

    private static JsonObject Semantics(Semantics s) => new()
    {
        ["relationships"] = Array(s.Relationships, Relationship)
    };

    private static SemanticRelationship Relationship(JsonObject o) => new(
        new SemanticRelationshipId(S(o, "id")),
        S(o, "type"),
        Arr(o, "entityRefs").Select(e => new SemanticEntityRef(S(e, "role"), S(e, "entityType"), S(e, "entityId"))).ToArray(),
        SourcedDictionary(o["properties"]),
        DoubleN(o, "confidence"),
        Strings(o["evidenceRefs"]).ToArray(),
        Provenance(O(o, "provenance")));

    private static JsonObject Relationship(SemanticRelationship r) => new()
    {
        ["id"] = r.Id.Value,
        ["type"] = r.Type,
        ["entityRefs"] = Array(r.EntityRefs, e => new JsonObject { ["role"] = e.Role, ["entityType"] = e.EntityType, ["entityId"] = e.EntityId }),
        ["properties"] = Dictionary(r.Properties, SourcedValue),
        ["confidence"] = r.Confidence,
        ["evidenceRefs"] = Array(r.EvidenceRefs, v => (JsonNode?)v),
        ["provenance"] = Provenance(r.Provenance)
    };

    private static PhysicalDesignState Physical(JsonObject o) => new(
        new PhysicalStateId(S(o, "stateId")),
        L(o, "stateRevision"),
        S(o, "status"),
        L(o, "basedOnProjectRevision"),
        Arr(o, "componentPoses").Select(ComponentPose).ToArray(),
        Arr(o, "routes").Select(Route).ToArray(),
        Arr(o, "vias").Select(Via).ToArray(),
        Arr(o, "copperZones").Select(CopperZone).ToArray(),
        DN(o, "lastModifiedAt"),
        SN(o, "lastModifiedBy"));

    private static JsonObject Physical(PhysicalDesignState p) => new()
    {
        ["stateId"] = p.StateId.Value,
        ["stateRevision"] = p.StateRevision,
        ["status"] = p.Status,
        ["basedOnProjectRevision"] = p.BasedOnProjectRevision,
        ["componentPoses"] = Array(p.ComponentPoses, ComponentPose),
        ["routes"] = Array(p.Routes, Route),
        ["vias"] = Array(p.Vias, Via),
        ["copperZones"] = Array(p.CopperZones, CopperZone),
        ["lastModifiedAt"] = p.LastModifiedAt?.ToString("O", CultureInfo.InvariantCulture),
        ["lastModifiedBy"] = p.LastModifiedBy
    };

    private static ComponentPose ComponentPose(JsonObject o) =>
        new(new ComponentId(S(o, "componentId")), new Point2(Units(o, "x"), Units(o, "y")), new AngleDegrees(Dec(o, "rotationDeg")), S(o, "side"), S(o, "placementState"), SN(o, "lastModifiedBy"));

    private static JsonObject ComponentPose(ComponentPose p) => new()
    {
        ["componentId"] = p.ComponentId.Value,
        ["x"] = p.Position.X.Value,
        ["y"] = p.Position.Y.Value,
        ["rotationDeg"] = p.Rotation.Value,
        ["side"] = p.Side,
        ["placementState"] = p.PlacementState,
        ["lastModifiedBy"] = p.LastModifiedBy
    };

    private static Route Route(JsonObject o) => new(
        new RouteId(S(o, "id")),
        new NetId(S(o, "netId")),
        S(o, "status"),
        S(o, "policy"),
        Arr(o, "trackSegments").Select(Track).ToArray(),
        Strings(o["viaIds"]).Select(v => new ViaId(v)).ToArray(),
        Provenance(O(o, "provenance")),
        Element(o["metadata"]));

    private static JsonObject Route(Route r) => new()
    {
        ["id"] = r.Id.Value,
        ["netId"] = r.NetId.Value,
        ["status"] = r.Status,
        ["policy"] = r.Policy,
        ["trackSegments"] = Array(r.TrackSegments, Track),
        ["viaIds"] = Array(r.ViaIds, v => (JsonNode?)v.Value),
        ["provenance"] = Provenance(r.Provenance),
        ["metadata"] = Node(r.Metadata)
    };

    private static TrackSegment Track(JsonObject o) => new(
        new TrackSegmentId(S(o, "id")),
        S(o, "geometryKind"),
        new LayerId(S(o, "layerId")),
        Units(o, "widthUnits"),
        Point(O(o, "start")),
        Point(O(o, "end")),
        o["arcCenter"] is JsonObject arc ? Point(arc) : null,
        BN(o, "clockwise"));

    private static JsonObject Track(TrackSegment t) => new()
    {
        ["id"] = t.Id.Value,
        ["geometryKind"] = t.GeometryKind,
        ["layerId"] = t.LayerId.Value,
        ["widthUnits"] = t.Width.Value,
        ["start"] = Point(t.Start),
        ["end"] = Point(t.End),
        ["arcCenter"] = t.ArcCenter is null ? null : Point(t.ArcCenter.Value),
        ["clockwise"] = t.Clockwise
    };

    private static Via Via(JsonObject o) => new(
        new ViaId(S(o, "id")),
        new NetId(S(o, "netId")),
        Point(O(o, "position")),
        new LayerId(S(o, "startLayerId")),
        new LayerId(S(o, "endLayerId")),
        S(o, "viaType"),
        Units(o, "drillDiameterUnits"),
        Units(o, "outerDiameterUnits"),
        S(o, "policy"),
        Element(o["padstackMetadata"]));

    private static JsonObject Via(Via v) => new()
    {
        ["id"] = v.Id.Value,
        ["netId"] = v.NetId.Value,
        ["position"] = Point(v.Position),
        ["startLayerId"] = v.StartLayerId.Value,
        ["endLayerId"] = v.EndLayerId.Value,
        ["viaType"] = v.ViaType,
        ["drillDiameterUnits"] = v.DrillDiameter.Value,
        ["outerDiameterUnits"] = v.OuterDiameter.Value,
        ["policy"] = v.Policy,
        ["padstackMetadata"] = Node(v.PadstackMetadata)
    };

    private static CopperZone CopperZone(JsonObject o) => new(
        new CopperZoneId(S(o, "id")),
        SN(o, "netId") is { } net ? new NetId(net) : null,
        new LayerId(S(o, "layerId")),
        Arr(o, "geometry").Select(Polygon).ToArray(),
        UnitsN(o, "clearanceUnits"),
        (int)(o["priority"]?.GetValue<long>() ?? 0),
        S(o, "policy"),
        Element(o["fillSettings"]));

    private static JsonObject CopperZone(CopperZone z) => new()
    {
        ["id"] = z.Id.Value,
        ["netId"] = z.NetId?.Value,
        ["layerId"] = z.LayerId.Value,
        ["geometry"] = Array(z.Geometry, Polygon),
        ["clearanceUnits"] = z.Clearance?.Value,
        ["priority"] = z.Priority,
        ["policy"] = z.Policy,
        ["fillSettings"] = Node(z.FillSettings)
    };

    private static ReviewDecision ReviewDecision(JsonObject o) =>
        new(new ReviewDecisionId(S(o, "id")), S(o, "decisionType"), S(o, "fingerprint"), Strings(o["entityRefs"]).ToArray(), SN(o, "reason"), D(o, "createdAt"), S(o, "createdBy"));

    private static JsonObject ReviewDecision(ReviewDecision d) => new()
    {
        ["id"] = d.Id.Value,
        ["decisionType"] = d.DecisionType,
        ["fingerprint"] = d.Fingerprint,
        ["entityRefs"] = Array(d.EntityRefs, v => (JsonNode?)v),
        ["reason"] = d.Reason,
        ["createdAt"] = d.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        ["createdBy"] = d.CreatedBy
    };

    private static ProjectSettings Settings(JsonObject o) =>
        new(Element(o["optimizationIntent"]), Arr(o, "exportProfiles").Select(Export).ToArray(), SN(o, "sourceEmbeddingPolicy"));

    private static JsonObject Settings(ProjectSettings s) => new()
    {
        ["optimizationIntent"] = Node(s.OptimizationIntent),
        ["exportProfiles"] = Array(s.ExportProfiles, Export),
        ["sourceEmbeddingPolicy"] = s.SourceEmbeddingPolicy
    };

    private static ExportProfile Export(JsonObject o) =>
        new(new StableId(S(o, "id")), S(o, "name"), S(o, "profileType"), Element(o["settings"]));

    private static JsonObject Export(ExportProfile e) => new()
    {
        ["id"] = e.Id.Value,
        ["name"] = e.Name,
        ["profileType"] = e.ProfileType,
        ["settings"] = Node(e.Settings)
    };

    private static Provenance Provenance(JsonObject o) =>
        new(S(o, "kind"), SN(o, "sourceRef"), SN(o, "model"), SN(o, "operation"), DN(o, "timestamp"), SN(o, "note"));

    private static JsonObject Provenance(Provenance p) => new()
    {
        ["kind"] = p.Kind,
        ["sourceRef"] = p.SourceRef,
        ["model"] = p.Model,
        ["operation"] = p.Operation,
        ["timestamp"] = p.Timestamp?.ToString("O", CultureInfo.InvariantCulture),
        ["note"] = p.Note
    };

    private static SourcedValue SourcedValue(JsonObject o) =>
        new(Element(o["value"]), SN(o, "unit"), S(o, "status"), DoubleN(o, "confidence"), Provenance(O(o, "provenance")));

    private static JsonObject SourcedValue(SourcedValue v) => new()
    {
        ["value"] = Node(v.Value),
        ["unit"] = v.Unit,
        ["status"] = v.Status,
        ["confidence"] = v.Confidence,
        ["provenance"] = Provenance(v.Provenance)
    };

    private static Diagnostic DiagnosticValue(JsonObject o) =>
        new(
            S(o, "code"),
            Enum.TryParse<DiagnosticSeverity>(S(o, "severity"), true, out var severity) ? severity : DiagnosticSeverity.Warning,
            S(o, "category"),
            S(o, "message"),
            Arr(o, "entityRefs").Select(e => new EntityReference(S(e, "entityType"), S(e, "entityId"))).ToArray(),
            EvidenceDictionary(o["evidence"]),
            SN(o, "remediation"),
            SN(o, "source"),
            B(o, "blocking"));

    private static JsonObject DiagnosticValue(Diagnostic d) => new()
    {
        ["code"] = d.Code,
        ["severity"] = d.Severity.ToString().ToUpperInvariant(),
        ["category"] = d.Category,
        ["message"] = d.Message,
        ["entityRefs"] = Array(d.EntityRefs ?? [], e => new JsonObject { ["entityType"] = e.EntityType, ["entityId"] = e.EntityId }),
        ["evidence"] = EvidenceObject(d.Evidence),
        ["remediation"] = d.Remediation,
        ["source"] = d.Source,
        ["blocking"] = d.Blocking
    };

    private static IReadOnlyDictionary<string, object?>? EvidenceDictionary(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return obj.ToDictionary(
            k => k.Key,
            v => v.Value is null ? null : (object?)JsonDocument.Parse(v.Value.ToJsonString()).RootElement.Clone(),
            StringComparer.Ordinal);
    }

    private static JsonObject EvidenceObject(IReadOnlyDictionary<string, object?>? evidence)
    {
        var obj = new JsonObject();
        if (evidence is null)
        {
            return obj;
        }

        foreach (var item in evidence.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            obj[item.Key] = item.Value switch
            {
                null => null,
                JsonElement element => Node(element),
                JsonNode node => node.DeepClone(),
                _ => JsonSerializer.SerializeToNode(item.Value)
            };
        }

        return obj;
    }

    private static IReadOnlyDictionary<string, SourcedValue> SourcedDictionary(JsonNode? node) =>
        Object(node).ToDictionary(k => k.Key, v => SourcedValue((JsonObject)v.Value!), StringComparer.Ordinal);

    private static JsonObject Dictionary<T>(IReadOnlyDictionary<string, T> dictionary, Func<T, JsonNode?> value)
    {
        var o = new JsonObject();
        foreach (var item in dictionary.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            o[item.Key] = value(item.Value);
        }

        return o;
    }

    private static JsonArray Array<T>(IEnumerable<T> values, Func<T, JsonNode?> value)
    {
        var a = new JsonArray();
        foreach (var item in values)
        {
            a.Add(value(item));
        }

        return a;
    }

    private static JsonObject Point(Point2 p) => new() { ["x"] = p.X.Value, ["y"] = p.Y.Value };

    private static Point2 Point(JsonObject o) => new(Units(o, "x"), Units(o, "y"));

    private static JsonObject? PolygonOrNull(Polygon2? p) => p is null ? null : Polygon(p);

    private static JsonObject Polygon(Polygon2 p) => new()
    {
        ["outer"] = Array(p.Outer, Point),
        ["holes"] = Array(p.Holes, h => Array(h, Point))
    };

    private static Polygon2? PolygonN(JsonNode? node) => node is JsonObject o ? Polygon(o) : null;

    private static Polygon2 Polygon(JsonObject o) =>
        new(ArrPoints(o["outer"]).ToArray(), Arr(o["holes"]).Select(h => ArrPoints(h).ToArray()).ToArray());

    private static IEnumerable<Point2> ArrPoints(JsonNode? node) => Arr(node).Select(Point);

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Object(JsonNode? node) =>
        node is JsonObject obj ? obj : new JsonObject();

    private static IEnumerable<JsonObject> Arr(JsonObject o, string name) => Arr(o[name]);

    private static IEnumerable<JsonObject> Arr(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(v => v?.GetValue<string>()).Where(v => v is not null).Select(v => v!)
            : [];

    private static JsonObject O(JsonObject o, string name) =>
        o[name] as JsonObject ?? new JsonObject();

    private static string S(JsonObject o, string name) =>
        o[name]?.GetValue<string>() ?? string.Empty;

    private static string? SN(JsonObject o, string name) =>
        o[name] is null || o[name] is JsonValue jv && jv.TryGetValue<object?>(out var value) && value is null
            ? null
            : o[name]?.GetValue<string>();

    private static long L(JsonObject o, string name) => o[name]?.GetValue<long>() ?? 0;

    private static decimal Dec(JsonObject o, string name) => o[name]?.GetValue<decimal>() ?? 0m;

    private static bool B(JsonObject o, string name) => o[name]?.GetValue<bool>() ?? false;

    private static bool? BN(JsonObject o, string name) => o[name] is null ? null : o[name]!.GetValue<bool>();

    private static DateTimeOffset D(JsonObject o, string name) =>
        DateTimeOffset.Parse(S(o, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static DateTimeOffset? DN(JsonObject o, string name) =>
        SN(o, name) is { } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null;

    private static double? DoubleN(JsonObject o, string name) => o[name] is null ? null : o[name]!.GetValue<double>();

    private static LengthUnits Units(JsonObject o, string name) => LengthUnits.FromMicrometers(L(o, name));

    private static LengthUnits? UnitsN(JsonObject o, string name) => o[name] is null ? null : LengthUnits.FromMicrometers(L(o, name));

    private static JsonElement Element(JsonNode? node) =>
        node is null ? JsonDefaults.NullElement : JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();

    private static JsonNode? Node(JsonElement element) =>
        JsonNode.Parse(element.GetRawText());
}
