using System.Text.Json.Nodes;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Prdx;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class CanonicalIntegrityValidator(SchemaRegistry? schemaRegistry = null) : ICanonicalProjectValidator
{
    private readonly SchemaRegistry _schemaRegistry = schemaRegistry ?? new SchemaRegistry();

    public ProjectValidationResult Validate(CanonicalProject project)
    {
        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(_schemaRegistry.ValidateProject(project.Root));
        if (diagnostics.Any(d => d.Blocking))
        {
            return new ProjectValidationResult(diagnostics);
        }

        diagnostics.AddRange(ValidateIntegrity(project.Root));
        return new ProjectValidationResult(diagnostics);
    }

    public IReadOnlyList<Diagnostic> ValidateIntegrity(JsonObject root)
    {
        var diagnostics = new List<Diagnostic>();
        var index = new ProjectIndex(root, diagnostics);

        ValidateComponents(root, index, diagnostics);
        ValidateNets(root, index, diagnostics);
        ValidateBoard(root, index, diagnostics);
        ValidateConstraints(root, index, diagnostics);
        ValidateSemantics(root, index, diagnostics);
        ValidatePhysicalState(root, index, diagnostics);

        return diagnostics;
    }

    private static void ValidateComponents(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var component in Objects(root["logicalDesign"]?["components"]))
        {
            var componentId = RequiredId(component, "id");
            var footprintId = component["footprintId"]?.GetValue<string>();
            if (footprintId is not null && !index.Footprints.ContainsKey(footprintId))
            {
                diagnostics.Add(RefNotFound("Component footprint does not exist.", "COMPONENT", componentId, "FOOTPRINT", footprintId));
            }
        }

        foreach (var footprint in Objects(root["logicalDesign"]?["footprints"]))
        {
            var footprintId = RequiredId(footprint, "id");
            foreach (var pad in Objects(footprint["pads"]))
            {
                var padId = RequiredId(pad, "id");
                foreach (var layerId in Strings(pad["layerIds"]))
                {
                    if (!index.Layers.ContainsKey(layerId))
                    {
                        diagnostics.Add(LayerNotFound("Pad references an unknown layer.", "PAD", padId, layerId));
                    }
                }

                if (pad["customPolygon"] is not null && pad["shape"]?.GetValue<string>() == "CUSTOM" && pad["customPolygon"] is not JsonObject)
                {
                    diagnostics.Add(Diagnostic.Error(DiagnosticCodes.ProjectSchema, "Schema", $"Custom pad '{padId}' requires polygon geometry."));
                }
            }

            foreach (var graphic in Objects(footprint["graphics"]))
            {
                var layerId = graphic["layerId"]?.GetValue<string>();
                if (layerId is not null && !index.Layers.ContainsKey(layerId))
                {
                    diagnostics.Add(LayerNotFound("Footprint graphic references an unknown layer.", "FOOTPRINT", footprintId, layerId));
                }
            }
        }
    }

    private static void ValidateNets(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var netClass in Objects(root["logicalDesign"]?["netClasses"]))
        {
            RequiredId(netClass, "id");
        }

        foreach (var net in Objects(root["logicalDesign"]?["netlist"]?["nets"]))
        {
            var netId = RequiredId(net, "id");
            var netClassId = net["netClassId"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(netClassId) && !index.NetClasses.Contains(netClassId))
            {
                diagnostics.Add(RefNotFound("Net class does not exist.", "NET", netId, "NET_CLASS", netClassId));
            }

            foreach (var endpoint in Objects(net["endpoints"]))
            {
                var componentId = endpoint["componentId"]?.GetValue<string>();
                var padId = endpoint["padId"]?.GetValue<string>();

                if (componentId is null || !index.Components.TryGetValue(componentId, out var footprintId))
                {
                    diagnostics.Add(RefNotFound("Net endpoint references an unknown component.", "NET", netId, "COMPONENT", componentId ?? "<missing>"));
                    continue;
                }

                if (padId is null || !index.PadToFootprint.TryGetValue(padId, out var padFootprintId))
                {
                    diagnostics.Add(RefNotFound("Net endpoint references an unknown pad.", "NET", netId, "PAD", padId ?? "<missing>"));
                    continue;
                }

                if (!StringComparer.Ordinal.Equals(footprintId, padFootprintId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PadFootprintMismatch,
                        DiagnosticSeverity.Error,
                        "Integrity",
                        $"Pad '{padId}' does not belong to component '{componentId}' footprint '{footprintId}'.",
                        [new EntityReference("NET", netId), new EntityReference("COMPONENT", componentId), new EntityReference("PAD", padId)],
                        Blocking: true));
                }
            }
        }
    }

    private static void ValidateBoard(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var stackupEntry in Objects(root["board"]?["stackup"]))
        {
            ValidateLayerReference(stackupEntry["layerId"]?.GetValue<string>(), "Stackup entry references an unknown layer.", "STACKUP", "stackup", index, diagnostics);
            foreach (var referenceLayerId in Strings(stackupEntry["referenceLayerIds"]))
            {
                ValidateLayerReference(referenceLayerId, "Stackup reference layer does not exist.", "STACKUP", "stackup", index, diagnostics);
            }
        }

        foreach (var region in Objects(root["board"]?["regions"]))
        {
            var regionId = RequiredId(region, "id");
            foreach (var layerId in Strings(region["layerIds"]))
            {
                ValidateLayerReference(layerId, "Region references an unknown layer.", "REGION", regionId, index, diagnostics);
            }
        }

        foreach (var keepout in Objects(root["board"]?["keepouts"]))
        {
            var keepoutId = RequiredId(keepout, "id");
            foreach (var layerId in Strings(keepout["layerIds"]))
            {
                ValidateLayerReference(layerId, "Keepout references an unknown layer.", "KEEPOUT", keepoutId, index, diagnostics);
            }
        }
    }

    private static void ValidateConstraints(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var constraint in Objects(root["constraints"]))
        {
            var constraintId = RequiredId(constraint, "id");
            ValidateSelector(constraint["source"], constraintId, index, diagnostics);
            ValidateSelector(constraint["target"], constraintId, index, diagnostics);

            foreach (var layerId in Strings(constraint["scope"]?["layerIds"]))
            {
                ValidateLayerReference(layerId, "Constraint scope references an unknown layer.", "CONSTRAINT", constraintId, index, diagnostics);
            }
        }
    }

    private static void ValidateSemantics(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var relationship in Objects(root["semantics"]?["relationships"]))
        {
            var relationshipId = RequiredId(relationship, "id");
            foreach (var entityRef in Objects(relationship["entityRefs"]))
            {
                var entityType = entityRef["entityType"]?.GetValue<string>();
                var entityId = entityRef["entityId"]?.GetValue<string>();
                if (entityType is not null && entityId is not null && !index.Exists(entityType, entityId))
                {
                    diagnostics.Add(RefNotFound("Semantic relationship references an unknown entity.", "SEMANTIC_RELATIONSHIP", relationshipId, entityType, entityId));
                }
            }
        }
    }

    private static void ValidatePhysicalState(JsonObject root, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        var seenPoses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pose in Objects(root["physicalDesignState"]?["componentPoses"]))
        {
            var componentId = pose["componentId"]?.GetValue<string>();
            if (componentId is null || !index.Components.ContainsKey(componentId))
            {
                diagnostics.Add(RefNotFound("Component pose references an unknown component.", "PHYSICAL_DESIGN_STATE", "componentPoses", "COMPONENT", componentId ?? "<missing>"));
            }
            else if (!seenPoses.Add(componentId))
            {
                diagnostics.Add(Duplicate("Duplicate component pose.", "COMPONENT", componentId));
            }
        }

        foreach (var via in Objects(root["physicalDesignState"]?["vias"]))
        {
            var viaId = RequiredId(via, "id");
            ValidateNetReference(via["netId"]?.GetValue<string>(), "Via references an unknown net.", "VIA", viaId, index, diagnostics);
            ValidateLayerReference(via["startLayerId"]?.GetValue<string>(), "Via start layer does not exist.", "VIA", viaId, index, diagnostics);
            ValidateLayerReference(via["endLayerId"]?.GetValue<string>(), "Via end layer does not exist.", "VIA", viaId, index, diagnostics);
        }

        foreach (var route in Objects(root["physicalDesignState"]?["routes"]))
        {
            var routeId = RequiredId(route, "id");
            var netId = route["netId"]?.GetValue<string>();
            ValidateNetReference(netId, "Route references an unknown net.", "ROUTE", routeId, index, diagnostics);

            foreach (var track in Objects(route["trackSegments"]))
            {
                var trackId = RequiredId(track, "id");
                ValidateLayerReference(track["layerId"]?.GetValue<string>(), "Track references an unknown layer.", "TRACK_SEGMENT", trackId, index, diagnostics);
            }

            foreach (var viaId in Strings(route["viaIds"]))
            {
                if (!index.Vias.ContainsKey(viaId))
                {
                    diagnostics.Add(RefNotFound("Route references an unknown via.", "ROUTE", routeId, "VIA", viaId));
                }
                else if (netId is not null && index.Vias[viaId] is { } viaNetId && !StringComparer.Ordinal.Equals(netId, viaNetId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RefNotFound,
                        DiagnosticSeverity.Error,
                        "Integrity",
                        $"Route '{routeId}' references via '{viaId}' that belongs to net '{viaNetId}'.",
                        [new EntityReference("ROUTE", routeId), new EntityReference("VIA", viaId)],
                        Blocking: true));
                }
            }
        }

        foreach (var zone in Objects(root["physicalDesignState"]?["copperZones"]))
        {
            var zoneId = RequiredId(zone, "id");
            var netId = zone["netId"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(netId))
            {
                ValidateNetReference(netId, "Copper zone references an unknown net.", "COPPER_ZONE", zoneId, index, diagnostics);
            }

            ValidateLayerReference(zone["layerId"]?.GetValue<string>(), "Copper zone references an unknown layer.", "COPPER_ZONE", zoneId, index, diagnostics);
        }
    }

    private static void ValidateSelector(JsonNode? selector, string constraintId, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (selector is not JsonObject selectorObject)
        {
            return;
        }

        var kind = selectorObject["kind"]?.GetValue<string>();
        if (kind != "ENTITY" && kind != "GROUP" && kind != "REGION")
        {
            return;
        }

        var entityType = kind == "ENTITY" ? selectorObject["entityType"]?.GetValue<string>() : kind;
        foreach (var entityId in Strings(selectorObject["entityIds"]))
        {
            if (entityType is not null && !index.Exists(entityType, entityId))
            {
                diagnostics.Add(RefNotFound("Constraint selector references an unknown entity.", "CONSTRAINT", constraintId, entityType, entityId));
            }
        }
    }

    private static void ValidateLayerReference(string? layerId, string message, string ownerType, string ownerId, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (layerId is null || !index.Layers.ContainsKey(layerId))
        {
            diagnostics.Add(LayerNotFound(message, ownerType, ownerId, layerId ?? "<missing>"));
        }
    }

    private static void ValidateNetReference(string? netId, string message, string ownerType, string ownerId, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (netId is null || !index.Nets.Contains(netId))
        {
            diagnostics.Add(RefNotFound(message, ownerType, ownerId, "NET", netId ?? "<missing>"));
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? array.Select(v => v?.GetValue<string>()).Where(v => v is not null).Select(v => v!)
            : [];

    private static string RequiredId(JsonObject obj, string propertyName) =>
        obj[propertyName]?.GetValue<string>() ?? "<missing>";

    private static Diagnostic RefNotFound(string message, string ownerType, string ownerId, string missingType, string missingId) =>
        new(
            DiagnosticCodes.RefNotFound,
            DiagnosticSeverity.Error,
            "Integrity",
            $"{message} Missing {missingType} '{missingId}'.",
            [new EntityReference(ownerType, ownerId)],
            Blocking: true);

    private static Diagnostic LayerNotFound(string message, string ownerType, string ownerId, string layerId) =>
        new(
            DiagnosticCodes.LayerNotFound,
            DiagnosticSeverity.Error,
            "Integrity",
            $"{message} Missing layer '{layerId}'.",
            [new EntityReference(ownerType, ownerId)],
            Blocking: true);

    private static Diagnostic Duplicate(string message, string entityType, string entityId) =>
        new(
            DiagnosticCodes.DuplicateId,
            DiagnosticSeverity.Error,
            "Integrity",
            $"{message} Duplicate {entityType} id '{entityId}'.",
            [new EntityReference(entityType, entityId)],
            Blocking: true);

    private sealed class ProjectIndex
    {
        public ProjectIndex(JsonObject root, List<Diagnostic> diagnostics)
        {
            AddMany(root["sourceImports"], "SOURCE_IMPORT", SourceImports, diagnostics);
            AddMany(root["logicalDesign"]?["netClasses"], "NET_CLASS", NetClasses, diagnostics);
            AddMany(root["logicalDesign"]?["groups"], "GROUP", Groups, diagnostics);
            AddMany(root["board"]?["layers"], "LAYER", Layers, diagnostics, layer => layer["layerType"]?.GetValue<string>() ?? string.Empty);
            AddMany(root["board"]?["regions"], "REGION", Regions, diagnostics);
            AddMany(root["board"]?["keepouts"], "KEEPOUT", Keepouts, diagnostics);
            AddMany(root["constraints"], "CONSTRAINT", Constraints, diagnostics);
            AddMany(root["physicalDesignState"]?["vias"], "VIA", Vias, diagnostics, via => via["netId"]?.GetValue<string>() ?? string.Empty);
            AddMany(root["physicalDesignState"]?["copperZones"], "COPPER_ZONE", CopperZones, diagnostics);
            AddMany(root["physicalDesignState"]?["routes"], "ROUTE", Routes, diagnostics);

            foreach (var footprint in Objects(root["logicalDesign"]?["footprints"]))
            {
                var footprintId = RequiredId(footprint, "id");
                Add(Footprints, "FOOTPRINT", footprintId, footprint, diagnostics, _ => footprintId);
                foreach (var pad in Objects(footprint["pads"]))
                {
                    Add(PadToFootprint, "PAD", RequiredId(pad, "id"), pad, diagnostics, _ => footprintId);
                }
            }

            foreach (var component in Objects(root["logicalDesign"]?["components"]))
            {
                Add(Components, "COMPONENT", RequiredId(component, "id"), component, diagnostics, c => c["footprintId"]?.GetValue<string>() ?? string.Empty);
            }

            foreach (var net in Objects(root["logicalDesign"]?["netlist"]?["nets"]))
            {
                var id = RequiredId(net, "id");
                if (!Nets.Add(id))
                {
                    diagnostics.Add(Duplicate("Duplicate net id.", "NET", id));
                }
            }
        }

        public Dictionary<string, string> SourceImports { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Components { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Footprints { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> PadToFootprint { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Nets { get; } = new(StringComparer.Ordinal);
        public HashSet<string> NetClasses { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Groups { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Layers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Regions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Keepouts { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Constraints { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Vias { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CopperZones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Routes { get; } = new(StringComparer.Ordinal);

        public bool Exists(string entityType, string entityId)
        {
            return Normalize(entityType) switch
            {
                "SOURCE_IMPORT" => SourceImports.ContainsKey(entityId),
                "COMPONENT" => Components.ContainsKey(entityId),
                "FOOTPRINT" => Footprints.ContainsKey(entityId),
                "PAD" => PadToFootprint.ContainsKey(entityId),
                "NET" => Nets.Contains(entityId),
                "NET_CLASS" => NetClasses.Contains(entityId),
                "GROUP" => Groups.Contains(entityId),
                "REGION" => Regions.Contains(entityId),
                "KEEPOUT" => Keepouts.Contains(entityId),
                "LAYER" => Layers.ContainsKey(entityId),
                "CONSTRAINT" => Constraints.Contains(entityId),
                "ROUTE" => Routes.Contains(entityId),
                "VIA" => Vias.ContainsKey(entityId),
                "COPPER_ZONE" => CopperZones.Contains(entityId),
                _ => true
            };
        }

        private static void AddMany(JsonNode? node, string entityType, HashSet<string> set, List<Diagnostic> diagnostics)
        {
            foreach (var obj in Objects(node))
            {
                var id = RequiredId(obj, "id");
                if (!set.Add(id))
                {
                    diagnostics.Add(Duplicate($"Duplicate {entityType} id.", entityType, id));
                }
            }
        }

        private static void AddMany(JsonNode? node, string entityType, Dictionary<string, string> map, List<Diagnostic> diagnostics, Func<JsonObject, string>? valueFactory = null)
        {
            foreach (var obj in Objects(node))
            {
                Add(map, entityType, RequiredId(obj, "id"), obj, diagnostics, valueFactory ?? (_ => string.Empty));
            }
        }

        private static void Add(Dictionary<string, string> map, string entityType, string id, JsonObject source, List<Diagnostic> diagnostics, Func<JsonObject, string> valueFactory)
        {
            if (!map.TryAdd(id, valueFactory(source)))
            {
                diagnostics.Add(Duplicate($"Duplicate {entityType} id.", entityType, id));
            }
        }

        private static string Normalize(string entityType) =>
            entityType.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant() switch
            {
                "COMPONENTS" => "COMPONENT",
                "NETS" => "NET",
                "LAYERS" => "LAYER",
                "REGIONS" => "REGION",
                "GROUP" => "GROUP",
                "REGION" => "REGION",
                var value => value
            };
    }
}
