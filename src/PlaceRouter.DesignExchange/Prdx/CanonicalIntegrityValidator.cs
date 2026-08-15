using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.DesignExchange.Prdx;

public sealed class CanonicalIntegrityValidator : ICanonicalProjectValidator
{
    public ProjectValidationResult Validate(CanonicalProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var index = new ProjectIndex(project, diagnostics);

        ValidateSources(project, index, diagnostics);
        ValidateComponents(project, index, diagnostics);
        ValidateNets(project, index, diagnostics);
        ValidateBoard(project, index, diagnostics);
        ValidateGroups(project, index, diagnostics);
        ValidateConstraints(project, index, diagnostics);
        ValidateSemantics(project, index, diagnostics);
        ValidatePhysicalState(project, index, diagnostics);

        return new ProjectValidationResult(diagnostics);
    }

    private static void ValidateSources(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var source in project.SourceImports)
        {
            if (source.EmbeddedPath is not null && !source.EmbeddedPath.StartsWith("source/", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.SupplementaryMissing,
                    "Integrity",
                    $"Source import '{source.Id}' points outside the source/ supplementary area.",
                    blocking: false));
            }
        }
    }

    private static void ValidateComponents(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var group in project.LogicalDesign.Components.GroupBy(c => c.ReferenceDesignator, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.RefdesDuplicate,
                    DiagnosticSeverity.Warning,
                    "Integrity",
                    $"Reference designator '{group.Key}' is used by more than one component.",
                    group.Select(c => new EntityReference("COMPONENT", c.Id.Value)).ToArray(),
                    Blocking: false));
            }
        }

        foreach (var component in project.LogicalDesign.Components)
        {
            if (component.FootprintId is null)
            {
                diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.FootprintUnresolved,
                    "Integrity",
                    $"Component '{component.Id}' does not have a resolved footprint.",
                    blocking: false));
            }
            else if (!index.Footprints.Contains(component.FootprintId.Value.Value))
            {
                diagnostics.Add(RefNotFound("Component footprint does not exist.", "COMPONENT", component.Id.Value, "FOOTPRINT", component.FootprintId.Value.Value));
            }
        }

        foreach (var footprint in project.LogicalDesign.Footprints)
        {
            foreach (var pad in footprint.Pads)
            {
                foreach (var layerId in pad.LayerIds)
                {
                    if (!index.Layers.ContainsKey(layerId.Value))
                    {
                        diagnostics.Add(LayerNotFound("Pad references an unknown layer.", "PAD", pad.Id.Value, layerId.Value));
                    }
                }
            }

            foreach (var graphic in footprint.Graphics)
            {
                if (!index.Layers.ContainsKey(graphic.LayerId.Value))
                {
                    diagnostics.Add(LayerNotFound("Footprint graphic references an unknown layer.", "FOOTPRINT", footprint.Id.Value, graphic.LayerId.Value));
                }
            }
        }
    }

    private static void ValidateNets(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var net in project.LogicalDesign.Nets)
        {
            if (net.NetClassId is not null && !index.NetClasses.Contains(net.NetClassId.Value.Value))
            {
                diagnostics.Add(RefNotFound("Net class does not exist.", "NET", net.Id.Value, "NET_CLASS", net.NetClassId.Value.Value));
            }

            foreach (var endpoint in net.Endpoints)
            {
                if (!index.Components.TryGetValue(endpoint.ComponentId.Value, out var footprintId))
                {
                    diagnostics.Add(RefNotFound("Net endpoint references an unknown component.", "NET", net.Id.Value, "COMPONENT", endpoint.ComponentId.Value));
                    continue;
                }

                if (endpoint.PadId is null)
                {
                    diagnostics.Add(Diagnostic.Warning(
                        DiagnosticCodes.PadMappingUnresolved,
                        "Integrity",
                        $"Net '{net.Id}' endpoint on component '{endpoint.ComponentId}' is preserved by pinRef but has no resolved pad.",
                        blocking: false));
                    continue;
                }

                if (!index.PadToFootprint.TryGetValue(endpoint.PadId.Value.Value, out var padFootprintId))
                {
                    diagnostics.Add(RefNotFound("Net endpoint references an unknown pad.", "NET", net.Id.Value, "PAD", endpoint.PadId.Value.Value));
                    continue;
                }

                if (footprintId is null)
                {
                    diagnostics.Add(Diagnostic.Warning(
                        DiagnosticCodes.FootprintUnresolved,
                        "Integrity",
                        $"Component '{endpoint.ComponentId}' endpoint pad cannot be checked until footprint is resolved.",
                        blocking: false));
                    continue;
                }

                if (!StringComparer.Ordinal.Equals(footprintId, padFootprintId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PadFootprintMismatch,
                        DiagnosticSeverity.Error,
                        "Integrity",
                        $"Pad '{endpoint.PadId}' does not belong to component '{endpoint.ComponentId}' footprint '{footprintId}'.",
                        [new EntityReference("NET", net.Id.Value), new EntityReference("COMPONENT", endpoint.ComponentId.Value), new EntityReference("PAD", endpoint.PadId.Value.Value)],
                        Blocking: true));
                }
            }
        }
    }

    private static void ValidateBoard(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var stackupEntry in project.Board.Stackup)
        {
            ValidateLayer(stackupEntry.LayerId.Value, "Stackup entry references an unknown layer.", "STACKUP", "stackup", index, diagnostics);
            foreach (var reference in stackupEntry.ReferenceLayerIds)
            {
                ValidateLayer(reference.Value, "Stackup reference layer does not exist.", "STACKUP", "stackup", index, diagnostics);
            }
        }

        foreach (var region in project.Board.Regions)
        {
            foreach (var layerId in region.LayerIds)
            {
                ValidateLayer(layerId.Value, "Region references an unknown layer.", "REGION", region.Id.Value, index, diagnostics);
            }
        }

        foreach (var keepout in project.Board.Keepouts)
        {
            foreach (var layerId in keepout.LayerIds)
            {
                ValidateLayer(layerId.Value, "Keepout references an unknown layer.", "KEEPOUT", keepout.Id.Value, index, diagnostics);
            }
        }
    }

    private static void ValidateGroups(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var group in project.LogicalDesign.Groups)
        {
            if (group.ParentGroupId is not null && !index.Groups.Contains(group.ParentGroupId.Value.Value))
            {
                diagnostics.Add(RefNotFound("Group parent does not exist.", "GROUP", group.Id.Value, "GROUP", group.ParentGroupId.Value.Value));
            }

            foreach (var member in group.Members)
            {
                ValidateEntityReference(index, diagnostics, "GROUP", group.Id.Value, member.EntityType, member.EntityId, "Group member references an unknown entity.");
            }
        }

        foreach (var group in project.LogicalDesign.Groups)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = group;
            while (current.ParentGroupId is not null)
            {
                if (!seen.Add(current.Id.Value) || current.ParentGroupId.Value.Value == group.Id.Value)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.GroupCycle,
                        DiagnosticSeverity.Error,
                        "Integrity",
                        $"Group hierarchy contains a cycle at group '{group.Id}'.",
                        [new EntityReference("GROUP", group.Id.Value)],
                        Blocking: true));
                    break;
                }

                if (!index.GroupMap.TryGetValue(current.ParentGroupId.Value.Value, out current!))
                {
                    break;
                }
            }
        }
    }

    private static void ValidateConstraints(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var constraint in project.Constraints)
        {
            ValidateSelector(constraint.Source, constraint.Id.Value, "sourceSelector", index, diagnostics);
            if (constraint.Target is not null)
            {
                ValidateSelector(constraint.Target, constraint.Id.Value, "targetSelector", index, diagnostics);
            }

            foreach (var layerId in constraint.Scope.LayerIds)
            {
                ValidateLayer(layerId.Value, "Constraint scope references an unknown layer.", "CONSTRAINT", constraint.Id.Value, index, diagnostics);
            }
        }
    }

    private static void ValidateSelector(ConstraintSelector selector, string constraintId, string selectorName, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        switch (selector.Kind)
        {
            case "ENTITY":
                var entityType = selector.EntityType ?? string.Empty;
                foreach (var entityId in selector.EntityIds)
                {
                    ValidateEntityReference(index, diagnostics, "CONSTRAINT", constraintId, entityType, entityId, $"Constraint {selectorName} references an unknown entity.");
                }
                break;

            case "GROUP":
                foreach (var entityId in selector.EntityIds)
                {
                    ValidateEntityReference(index, diagnostics, "CONSTRAINT", constraintId, "GROUP", entityId, $"Constraint {selectorName} references an unknown group.");
                }
                break;

            case "REGION":
                foreach (var entityId in selector.EntityIds)
                {
                    ValidateEntityReference(index, diagnostics, "CONSTRAINT", constraintId, "REGION", entityId, $"Constraint {selectorName} references an unknown region.");
                }
                break;

            case "CLASS":
                if (!string.IsNullOrWhiteSpace(selector.EntityType))
                {
                    foreach (var entityId in selector.EntityIds)
                    {
                        ValidateEntityReference(index, diagnostics, "CONSTRAINT", constraintId, selector.EntityType, entityId, $"Constraint {selectorName} references an unknown class entity.");
                    }
                }
                break;
        }
    }

    private static void ValidateSemantics(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        foreach (var relationship in project.Semantics.Relationships)
        {
            foreach (var entityRef in relationship.EntityRefs)
            {
                ValidateEntityReference(index, diagnostics, "SEMANTIC_RELATIONSHIP", relationship.Id.Value, entityRef.EntityType, entityRef.EntityId, "Semantic relationship references an unknown entity.");
            }
        }
    }

    private static void ValidatePhysicalState(CanonicalProject project, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (project.PhysicalDesignState.BasedOnProjectRevision > project.ProjectRevision)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.RefNotFound,
                DiagnosticSeverity.Error,
                "Integrity",
                "PhysicalDesignState basedOnProjectRevision points to a future project revision.",
                Blocking: true));
        }

        foreach (var poseGroup in project.PhysicalDesignState.ComponentPoses.GroupBy(p => p.ComponentId))
        {
            if (poseGroup.Count() > 1)
            {
                diagnostics.Add(Duplicate("Duplicate component pose.", "COMPONENT", poseGroup.Key.Value));
            }
        }

        foreach (var pose in project.PhysicalDesignState.ComponentPoses)
        {
            if (!index.Components.ContainsKey(pose.ComponentId.Value))
            {
                diagnostics.Add(RefNotFound("Component pose references an unknown component.", "PHYSICAL_DESIGN_STATE", "componentPoses", "COMPONENT", pose.ComponentId.Value));
            }
        }

        foreach (var via in project.PhysicalDesignState.Vias)
        {
            if (!index.Nets.Contains(via.NetId.Value))
            {
                diagnostics.Add(RefNotFound("Via references an unknown net.", "VIA", via.Id.Value, "NET", via.NetId.Value));
            }

            ValidateCopperLayer(via.StartLayerId.Value, "Via start layer does not exist or is not copper.", "VIA", via.Id.Value, index, diagnostics);
            ValidateCopperLayer(via.EndLayerId.Value, "Via end layer does not exist or is not copper.", "VIA", via.Id.Value, index, diagnostics);
        }

        foreach (var route in project.PhysicalDesignState.Routes)
        {
            if (!index.Nets.Contains(route.NetId.Value))
            {
                diagnostics.Add(RefNotFound("Route references an unknown net.", "ROUTE", route.Id.Value, "NET", route.NetId.Value));
            }

            foreach (var track in route.TrackSegments)
            {
                ValidateCopperLayer(track.LayerId.Value, "Track references an unknown or non-copper layer.", "TRACK_SEGMENT", track.Id.Value, index, diagnostics);
            }

            foreach (var viaId in route.ViaIds)
            {
                if (!index.Vias.TryGetValue(viaId.Value, out var viaNetId))
                {
                    diagnostics.Add(RefNotFound("Route references an unknown via.", "ROUTE", route.Id.Value, "VIA", viaId.Value));
                }
                else if (!StringComparer.Ordinal.Equals(route.NetId.Value, viaNetId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RouteViaNetMismatch,
                        DiagnosticSeverity.Error,
                        "Integrity",
                        $"Route '{route.Id}' references via '{viaId}' that belongs to net '{viaNetId}'.",
                        [new EntityReference("ROUTE", route.Id.Value), new EntityReference("VIA", viaId.Value)],
                        Blocking: true));
                }
            }
        }

        foreach (var zone in project.PhysicalDesignState.CopperZones)
        {
            if (zone.NetId is not null && !index.Nets.Contains(zone.NetId.Value.Value))
            {
                diagnostics.Add(RefNotFound("Copper zone references an unknown net.", "COPPER_ZONE", zone.Id.Value, "NET", zone.NetId.Value.Value));
            }

            ValidateCopperLayer(zone.LayerId.Value, "Copper zone references an unknown or non-copper layer.", "COPPER_ZONE", zone.Id.Value, index, diagnostics);
        }
    }

    private static void ValidateLayer(string layerId, string message, string ownerType, string ownerId, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (!index.Layers.ContainsKey(layerId))
        {
            diagnostics.Add(LayerNotFound(message, ownerType, ownerId, layerId));
        }
    }

    private static void ValidateCopperLayer(string layerId, string message, string ownerType, string ownerId, ProjectIndex index, List<Diagnostic> diagnostics)
    {
        if (!index.Layers.TryGetValue(layerId, out var layer))
        {
            diagnostics.Add(LayerNotFound(message, ownerType, ownerId, layerId));
            return;
        }

        if (!layer.IsCopperCapable)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayerNotCopper,
                DiagnosticSeverity.Error,
                "Integrity",
                $"{message} Layer '{layerId}' type is '{layer.LayerType}'.",
                [new EntityReference(ownerType, ownerId)],
                Blocking: true));
        }
    }

    private static void ValidateEntityReference(ProjectIndex index, List<Diagnostic> diagnostics, string ownerType, string ownerId, string entityType, string entityId, string missingMessage)
    {
        if (index.TryExists(entityType, entityId, out var knownType))
        {
            return;
        }

        diagnostics.Add(knownType
            ? RefNotFound(missingMessage, ownerType, ownerId, entityType, entityId)
            : UnknownEntityType(ownerType, ownerId, entityType));
    }

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

    private static Diagnostic UnknownEntityType(string ownerType, string ownerId, string entityType) =>
        new(
            DiagnosticCodes.EntityTypeUnknown,
            DiagnosticSeverity.Error,
            "Integrity",
            $"Unknown entity type '{entityType}'.",
            [new EntityReference(ownerType, ownerId)],
            Blocking: true);

    private sealed class ProjectIndex
    {
        public ProjectIndex(CanonicalProject project, List<Diagnostic> diagnostics)
        {
            AddMany(project.SourceImports.Select(x => x.Id.Value), "SOURCE_IMPORT", SourceImports, diagnostics);
            AddMany(project.LogicalDesign.Footprints.Select(x => x.Id.Value), "FOOTPRINT", Footprints, diagnostics);
            AddMany(project.LogicalDesign.Nets.Select(x => x.Id.Value), "NET", Nets, diagnostics);
            AddMany(project.LogicalDesign.NetClasses.Select(x => x.Id.Value), "NET_CLASS", NetClasses, diagnostics);
            AddMany(project.LogicalDesign.Groups.Select(x => x.Id.Value), "GROUP", Groups, diagnostics);
            AddMany(project.Board.Regions.Select(x => x.Id.Value), "REGION", Regions, diagnostics);
            AddMany(project.Board.Keepouts.Select(x => x.Id.Value), "KEEPOUT", Keepouts, diagnostics);
            AddMany(project.Constraints.Select(x => x.Id.Value), "CONSTRAINT", Constraints, diagnostics);
            AddMany(project.Semantics.Relationships.Select(x => x.Id.Value), "SEMANTIC_RELATIONSHIP", SemanticRelationships, diagnostics);
            AddMany(project.PhysicalDesignState.Routes.Select(x => x.Id.Value), "ROUTE", Routes, diagnostics);
            AddMany(project.PhysicalDesignState.CopperZones.Select(x => x.Id.Value), "COPPER_ZONE", CopperZones, diagnostics);
            AddMany(project.ReviewDecisions.Select(x => x.Id.Value), "REVIEW_DECISION", ReviewDecisions, diagnostics);
            AddMany(project.Board.Holes.Select(x => x.Id.Value), "BOARD_HOLE", BoardHoles, diagnostics);

            foreach (var layer in project.Board.Layers)
            {
                if (!Layers.TryAdd(layer.Id.Value, layer))
                {
                    diagnostics.Add(Duplicate("Duplicate layer id.", "LAYER", layer.Id.Value));
                }
            }

            foreach (var footprint in project.LogicalDesign.Footprints)
            {
                foreach (var pad in footprint.Pads)
                {
                    if (!PadToFootprint.TryAdd(pad.Id.Value, footprint.Id.Value))
                    {
                        diagnostics.Add(Duplicate("Duplicate pad id.", "PAD", pad.Id.Value));
                    }
                }
            }

            foreach (var component in project.LogicalDesign.Components)
            {
                if (!Components.TryAdd(component.Id.Value, component.FootprintId?.Value))
                {
                    diagnostics.Add(Duplicate("Duplicate component id.", "COMPONENT", component.Id.Value));
                }
            }

            foreach (var group in project.LogicalDesign.Groups)
            {
                GroupMap[group.Id.Value] = group;
            }

            foreach (var via in project.PhysicalDesignState.Vias)
            {
                if (!Vias.TryAdd(via.Id.Value, via.NetId.Value))
                {
                    diagnostics.Add(Duplicate("Duplicate via id.", "VIA", via.Id.Value));
                }
            }

            foreach (var track in project.PhysicalDesignState.Routes.SelectMany(route => route.TrackSegments))
            {
                if (!TrackSegments.Add(track.Id.Value))
                {
                    diagnostics.Add(Duplicate("Duplicate track segment id.", "TRACK_SEGMENT", track.Id.Value));
                }
            }
        }

        public HashSet<string> SourceImports { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string?> Components { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Footprints { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> PadToFootprint { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Nets { get; } = new(StringComparer.Ordinal);
        public HashSet<string> NetClasses { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Groups { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Group> GroupMap { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BoardLayer> Layers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Regions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Keepouts { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Constraints { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SemanticRelationships { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Vias { get; } = new(StringComparer.Ordinal);
        public HashSet<string> TrackSegments { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CopperZones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Routes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ReviewDecisions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> BoardHoles { get; } = new(StringComparer.Ordinal);

        public bool TryExists(string entityType, string entityId, out bool knownType)
        {
            knownType = true;
            return Normalize(entityType) switch
            {
                "SOURCE_IMPORT" => SourceImports.Contains(entityId),
                "COMPONENT" => Components.ContainsKey(entityId),
                "FOOTPRINT" => Footprints.Contains(entityId),
                "PAD" => PadToFootprint.ContainsKey(entityId),
                "NET" => Nets.Contains(entityId),
                "NET_CLASS" => NetClasses.Contains(entityId),
                "GROUP" => Groups.Contains(entityId),
                "REGION" => Regions.Contains(entityId),
                "KEEPOUT" => Keepouts.Contains(entityId),
                "LAYER" => Layers.ContainsKey(entityId),
                "CONSTRAINT" => Constraints.Contains(entityId),
                "SEMANTIC_RELATIONSHIP" => SemanticRelationships.Contains(entityId),
                "ROUTE" => Routes.Contains(entityId),
                "VIA" => Vias.ContainsKey(entityId),
                "TRACK_SEGMENT" => TrackSegments.Contains(entityId),
                "COPPER_ZONE" => CopperZones.Contains(entityId),
                "REVIEW_DECISION" => ReviewDecisions.Contains(entityId),
                "BOARD_HOLE" => BoardHoles.Contains(entityId),
                _ => Unknown(out knownType)
            };
        }

        private static void AddMany(IEnumerable<string> ids, string entityType, HashSet<string> set, List<Diagnostic> diagnostics)
        {
            foreach (var id in ids)
            {
                if (!set.Add(id))
                {
                    diagnostics.Add(Duplicate($"Duplicate {entityType} id.", entityType, id));
                }
            }
        }

        private static string Normalize(string entityType) =>
            entityType.Trim().Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant() switch
            {
                "COMPONENTS" => "COMPONENT",
                "NETS" => "NET",
                "LAYERS" => "LAYER",
                "REGIONS" => "REGION",
                "TRACK" => "TRACK_SEGMENT",
                "TRACKS" => "TRACK_SEGMENT",
                "TRACK_SEGMENTS" => "TRACK_SEGMENT",
                "SEMANTIC_RELATIONSHIPS" => "SEMANTIC_RELATIONSHIP",
                "REVIEW_DECISIONS" => "REVIEW_DECISION",
                "BOARD_HOLES" => "BOARD_HOLE",
                var value => value
            };

        private static bool Unknown(out bool knownType)
        {
            knownType = false;
            return false;
        }
    }
}
