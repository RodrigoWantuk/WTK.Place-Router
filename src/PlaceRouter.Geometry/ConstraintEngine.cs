using System.Text.Json;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.Geometry;

public sealed record EffectiveConstraint(
    ConstraintId Id,
    string Type,
    ConstraintSelector Source,
    ConstraintSelector? Target,
    JsonElement Parameters,
    string Enforcement,
    ConstraintScope Scope,
    Provenance Provenance,
    string EffectiveSource,
    int Specificity);

public sealed class ConstraintSelectorResolver
{
    private readonly CanonicalProject _project;

    public ConstraintSelectorResolver(CanonicalProject project)
    {
        _project = project;
    }

    public IReadOnlyList<EntityReference> Resolve(ConstraintSelector selector)
    {
        return Canon(selector.Kind) switch
        {
            "ALL" => All(selector.EntityType),
            "ENTITY" => selector.EntityIds.Select(id => new EntityReference(selector.EntityType ?? string.Empty, id)).ToArray(),
            "GROUP" => selector.EntityIds.SelectMany(ResolveGroup).Distinct().ToArray(),
            "REGION" => selector.EntityIds.Select(id => new EntityReference("REGION", id)).ToArray(),
            "CLASS" => ResolveClass(selector),
            _ => []
        };
    }

    private IReadOnlyList<EntityReference> All(string? entityType)
    {
        var type = Canon(entityType ?? "COMPONENT");
        return type switch
        {
            "COMPONENT" => _project.LogicalDesign.Components.Select(c => new EntityReference("COMPONENT", c.Id.Value)).ToArray(),
            "NET" => _project.LogicalDesign.Nets.Select(n => new EntityReference("NET", n.Id.Value)).ToArray(),
            "TRACKSEGMENT" => _project.PhysicalDesignState.Routes.SelectMany(r => r.TrackSegments.Select(t => new EntityReference("TRACK_SEGMENT", t.Id.Value))).ToArray(),
            "ROUTE" => _project.PhysicalDesignState.Routes.Select(r => new EntityReference("ROUTE", r.Id.Value)).ToArray(),
            "VIA" => _project.PhysicalDesignState.Vias.Select(v => new EntityReference("VIA", v.Id.Value)).ToArray(),
            _ => []
        };
    }

    private IEnumerable<EntityReference> ResolveGroup(string groupId)
    {
        var group = _project.LogicalDesign.Groups.FirstOrDefault(g => g.Id.Value == groupId);
        if (group is null)
        {
            yield break;
        }

        foreach (var member in group.Members)
        {
            if (Canon(member.EntityType) == "GROUP")
            {
                foreach (var child in ResolveGroup(member.EntityId))
                {
                    yield return child;
                }
            }
            else
            {
                yield return new EntityReference(member.EntityType, member.EntityId);
            }
        }
    }

    private IReadOnlyList<EntityReference> ResolveClass(ConstraintSelector selector)
    {
        if (Canon(selector.EntityType ?? string.Empty) == "NETCLASS")
        {
            return _project.LogicalDesign.Nets
                .Where(n => n.NetClassId is not null && selector.EntityIds.Contains(n.NetClassId.Value.Value, StringComparer.Ordinal))
                .Select(n => new EntityReference("NET", n.Id.Value))
                .ToArray();
        }

        return [];
    }

    internal static string Canon(string value) =>
        value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

public sealed class EffectiveConstraintResolver
{
    public (IReadOnlyList<EffectiveConstraint> Constraints, IReadOnlyList<ConstraintConflict> Conflicts) Resolve(CanonicalProject project, ManufacturingRules manufacturingRules)
    {
        var constraints = new List<EffectiveConstraint>();
        constraints.AddRange(ManufacturingConstraints(project, manufacturingRules));
        constraints.AddRange(project.Constraints.Where(c => c.Enabled).Select(c => new EffectiveConstraint(c.Id, c.Type, c.Source, c.Target, c.Parameters, c.Enforcement, c.Scope, c.Provenance, c.Provenance.Kind, Specificity(c.Source) + (c.Target is null ? 0 : Specificity(c.Target)))));

        return (constraints, DetectConflicts(constraints));
    }

    private static IEnumerable<EffectiveConstraint> ManufacturingConstraints(CanonicalProject project, ManufacturingRules rules)
    {
        var scope = new ConstraintScope([], null, null, []);
        var source = new ConstraintSelector("ALL", null, [], null);
        yield return Mfg("mfg_min_track_width", "MinimumTrackWidth", source, Param("minimumUnits", rules.MinimumTrackWidth.Value), rules, scope);
        yield return Mfg("mfg_min_clearance", "MinimumClearance", source, Param("minimumUnits", rules.MinimumClearance.Value), rules, scope);
        yield return Mfg("mfg_copper_to_edge", "CopperToEdge", source, Param("minimumUnits", rules.CopperToEdge.Value), rules, scope);
        yield return Mfg("mfg_min_drill", "MinimumDrill", source, Param("minimumUnits", rules.MinimumDrill.Value), rules, scope);
        yield return Mfg("mfg_min_via_diameter", "MinimumViaDiameter", source, Param("minimumUnits", rules.MinimumViaDiameter.Value), rules, scope);

        static EffectiveConstraint Mfg(string id, string type, ConstraintSelector source, JsonElement parameters, ManufacturingRules rules, ConstraintScope scope) =>
            new(new ConstraintId(id), type, source, null, parameters, "REQUIRED", scope, rules.Provenance, "ManufacturingProfile", 1);
    }

    private static IReadOnlyList<ConstraintConflict> DetectConflicts(IReadOnlyList<EffectiveConstraint> constraints)
    {
        var conflicts = new List<ConstraintConflict>();
        var required = constraints.Where(c => string.Equals(c.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var pair in required.SelectMany((a, i) => required.Skip(i + 1).Select(b => (a, b))))
        {
            if (!SameSelector(pair.a.Source, pair.b.Source))
            {
                continue;
            }

            var type = ConstraintSelectorResolver.Canon(pair.a.Type);
            if (type != ConstraintSelectorResolver.Canon(pair.b.Type))
            {
                continue;
            }

            if (type == "ALLOWEDROTATION")
            {
                var left = NumericSet(pair.a.Parameters, "allowedDegrees", "rotations");
                var right = NumericSet(pair.b.Parameters, "allowedDegrees", "rotations");
                if (left.Count > 0 && right.Count > 0 && !left.Intersect(right).Any())
                {
                    conflicts.Add(new ConstraintConflict(pair.a.Id, pair.b.Id, "Required allowed rotation constraints have no common value.", ConstraintEvidence.From(("left", string.Join(",", left)), ("right", string.Join(",", right)))));
                }
            }

            if (type == "ALLOWEDSIDE")
            {
                var left = StringSet(pair.a.Parameters, "allowedSides", "sides", "side");
                var right = StringSet(pair.b.Parameters, "allowedSides", "sides", "side");
                if (left.Count > 0 && right.Count > 0 && !left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any())
                {
                    conflicts.Add(new ConstraintConflict(pair.a.Id, pair.b.Id, "Required allowed side constraints have no common value.", ConstraintEvidence.From(("left", string.Join(",", left)), ("right", string.Join(",", right)))));
                }
            }
        }

        return conflicts;
    }

    private static bool SameSelector(ConstraintSelector left, ConstraintSelector right) =>
        string.Equals(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.EntityType, right.EntityType, StringComparison.OrdinalIgnoreCase) &&
        left.EntityIds.Order(StringComparer.Ordinal).SequenceEqual(right.EntityIds.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static int Specificity(ConstraintSelector selector) =>
        ConstraintSelectorResolver.Canon(selector.Kind) switch
        {
            "ALL" => 0,
            "CLASS" => 2,
            "GROUP" => 3,
            "REGION" => 3,
            "ENTITY" => 4,
            _ => 1
        };

    internal static JsonElement Param(string key, long value) =>
        JsonDocument.Parse($$"""{"{{key}}":{{value}}}""").RootElement.Clone();

    internal static IReadOnlySet<decimal> NumericSet(JsonElement parameters, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parameters.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDecimal()).ToHashSet();
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return new HashSet<decimal> { value.GetDecimal() };
            }
        }

        return new HashSet<decimal>();
    }

    internal static IReadOnlySet<string> StringSet(JsonElement parameters, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parameters.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value.GetString()! };
            }
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ConstraintEvaluationService
{
    private readonly IGeometryKernel _kernel;
    private readonly PhysicalGeometryBuilder _geometryBuilder;
    private readonly EffectiveConstraintResolver _resolver = new();
    private readonly ReadinessAnalyzer _readinessAnalyzer = new();

    public ConstraintEvaluationService(IGeometryKernel? kernel = null)
    {
        _kernel = kernel ?? new ClipperGeometryKernel();
        _geometryBuilder = new PhysicalGeometryBuilder(_kernel);
    }

    public ConstraintEvaluationReport Evaluate(CanonicalProject project)
    {
        var geometry = _geometryBuilder.Build(project);
        var rules = ManufacturingRuleResolver.Resolve(project.ManufacturingProfile);
        var (constraints, conflicts) = _resolver.Resolve(project, rules);
        var resolver = new ConstraintSelectorResolver(project);
        var evaluations = constraints.SelectMany(c => EvaluateConstraint(project, geometry, resolver, c, rules)).ToArray();
        var readiness = _readinessAnalyzer.Analyze(project, geometry);
        var findings = Findings(evaluations, conflicts).ToArray();
        return new ConstraintEvaluationReport(evaluations, conflicts, readiness, findings);
    }

    private IEnumerable<ConstraintEvaluation> EvaluateConstraint(CanonicalProject project, PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, ManufacturingRules rules)
    {
        var type = ConstraintSelectorResolver.Canon(constraint.Type);
        return type switch
        {
            "BOARDBOUNDS" => BoardBounds(geometry, resolver, constraint),
            "COMPONENTOVERLAP" or "COURTYARD" => ComponentOverlap(geometry, resolver, constraint),
            "KEEPOUT" => Keepout(geometry, resolver, constraint),
            "FIXEDPOSITION" or "LOCKED" => FixedPosition(geometry, resolver, constraint),
            "ALLOWEDROTATION" or "ALLOWEDROTATIONS" => AllowedRotation(geometry, resolver, constraint),
            "ALLOWEDSIDE" => AllowedSide(geometry, resolver, constraint),
            "MINIMUMSEPARATION" => MinimumSeparation(geometry, resolver, constraint, rules.MinimumComponentSpacing),
            "INSIDEREGION" => InsideOutsideRegion(geometry, resolver, constraint, inside: true),
            "OUTSIDEREGION" => InsideOutsideRegion(geometry, resolver, constraint, inside: false),
            "MINIMUMTRACKWIDTH" or "MINIMUMWIDTH" => MinimumTrackWidth(project, resolver, constraint, rules.MinimumTrackWidth),
            "MINIMUMCLEARANCE" => MinimumClearance(geometry, constraint, rules.MinimumClearance),
            "COPPERTOEDGE" => CopperToEdge(geometry, constraint, rules.CopperToEdge),
            "MINIMUMDRILL" => MinimumDrill(project, constraint, rules.MinimumDrill),
            "MINIMUMVIADIAMETER" => MinimumViaDiameter(project, constraint, rules.MinimumViaDiameter),
            "MAXIMUMVIAS" => MaximumVias(project, resolver, constraint),
            "MAXIMUMLENGTH" => MaximumLength(project, resolver, constraint),
            _ => [Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], null, null, ConstraintEvidence.From(("reason", "No evaluator registered for constraint type.")), null)]
        };
    }

    private IEnumerable<ConstraintEvaluation> BoardBounds(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var board = geometry.BoardObjects.FirstOrDefault(o => o.Kind == PhysicalObjectKind.Board);
        if (board is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "board.outline")), "Board outline is required.");
            yield break;
        }

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var boundary = component.PlacementBoundary;
            if (boundary is null)
            {
                yield return UnknownGeometry(constraint, "COMPONENT", component.Component.Id.Value, "Component has no body or courtyard geometry.");
                continue;
            }

            var pass = _kernel.Contains(board.Geometry, boundary);
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("board", board.Id)), pass ? null : "Component is outside board outline.");
        }
    }

    private IEnumerable<ConstraintEvaluation> ComponentOverlap(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var components = Components(geometry, resolver.Resolve(constraint.Source)).ToArray();
        if (components.Length == 0)
        {
            yield return UnknownSelector(constraint);
            yield break;
        }

        var any = false;
        for (var i = 0; i < components.Length; i++)
        {
            for (var j = i + 1; j < components.Length; j++)
            {
                var a = components[i].PlacementBoundary;
                var b = components[j].PlacementBoundary;
                if (a is null || b is null)
                {
                    yield return UnknownGeometry(constraint, "COMPONENT", a is null ? components[i].Component.Id.Value : components[j].Component.Id.Value, "Component has no body or courtyard geometry.");
                    any = true;
                    continue;
                }

                var overlaps = _kernel.Intersects(a, b);
                yield return Result(constraint, overlaps ? ConstraintEvaluationStatus.Fail : ConstraintEvaluationStatus.Pass, [new EntityReference("COMPONENT", components[i].Component.Id.Value), new EntityReference("COMPONENT", components[j].Component.Id.Value)], null, overlaps ? new LengthUnits(0) : _kernel.Distance(a, b), ConstraintEvidence.From(("phase", "exact")), overlaps ? "Component courtyards overlap." : null);
                any = true;
            }
        }

        if (!any)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], null, null, ConstraintEvidence.Empty, null);
        }
    }

    private IEnumerable<ConstraintEvaluation> Keepout(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var boundary = component.PlacementBoundary;
            if (boundary is null)
            {
                yield return UnknownGeometry(constraint, "COMPONENT", component.Component.Id.Value, "Component has no body or courtyard geometry.");
                continue;
            }

            var hit = geometry.KeepoutObjects.FirstOrDefault(k => _kernel.Intersects(boundary, k.Geometry));
            yield return Result(constraint, hit is null ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("keepout", hit?.Id)), hit is null ? null : "Component intersects keepout.");
        }
    }

    private IEnumerable<ConstraintEvaluation> FixedPosition(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var expectedX = LongParam(constraint.Parameters, "xUnits", "x");
            var expectedY = LongParam(constraint.Parameters, "yUnits", "y");
            var locked = string.Equals(component.Pose.PlacementState, "LOCKED", StringComparison.OrdinalIgnoreCase) || string.Equals(component.Component.PlacementPolicy, "LOCKED", StringComparison.OrdinalIgnoreCase);
            var atExpected = expectedX is null || expectedY is null || (component.Pose.Position.X.Value == expectedX && component.Pose.Position.Y.Value == expectedY);
            yield return Result(constraint, locked && atExpected ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("locked", locked), ("x", component.Pose.Position.X.Value), ("y", component.Pose.Position.Y.Value)), locked && atExpected ? null : "Component is not locked at the required position.");
        }
    }

    private IEnumerable<ConstraintEvaluation> AllowedRotation(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var allowed = EffectiveConstraintResolver.NumericSet(constraint.Parameters, "allowedDegrees", "rotations");
        if (allowed.Count == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Fail, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("allowedDegrees", "empty")), "Allowed rotation set is empty.");
            yield break;
        }

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var pass = allowed.Contains(Normalize(component.Pose.Rotation.Value));
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("actualDegrees", component.Pose.Rotation.Value), ("allowedDegrees", string.Join(",", allowed))), pass ? null : "Component rotation is not allowed.");
        }
    }

    private IEnumerable<ConstraintEvaluation> AllowedSide(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var allowed = EffectiveConstraintResolver.StringSet(constraint.Parameters, "allowedSides", "sides", "side");
        if (allowed.Count == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Fail, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("allowedSides", "empty")), "Allowed side set is empty.");
            yield break;
        }

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var pass = allowed.Contains(component.Pose.Side);
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("actualSide", component.Pose.Side), ("allowedSides", string.Join(",", allowed))), pass ? null : "Component side is not allowed.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumSeparation(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var required = new LengthUnits(LongParam(constraint.Parameters, "distanceUnits", "minimumUnits", "clearanceUnits") ?? fallback.Value);
        var sources = Components(geometry, resolver.Resolve(constraint.Source)).ToArray();
        var targetRefs = constraint.Target is null ? [] : resolver.Resolve(constraint.Target);
        var targets = constraint.Target is null ? sources.Skip(1).ToArray() : Components(geometry, targetRefs).ToArray();
        var targetNetIds = targetRefs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var sourceBoundary = source.PlacementBoundary;
            if (sourceBoundary is null)
            {
                yield return UnknownGeometry(constraint, "COMPONENT", source.Component.Id.Value, "Component has no body or courtyard geometry.");
                continue;
            }

            foreach (var target in targets.Where(t => t.Component.Id != source.Component.Id))
            {
                var targetBoundary = target.PlacementBoundary;
                if (targetBoundary is null)
                {
                    yield return UnknownGeometry(constraint, "COMPONENT", target.Component.Id.Value, "Component has no body or courtyard geometry.");
                    continue;
                }

                var actual = _kernel.Distance(sourceBoundary, targetBoundary);
                yield return Result(constraint, actual.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference("COMPONENT", target.Component.Id.Value)], required, actual, ConstraintEvidence.From(("measurement", "nearest-geometry")), actual.Value >= required.Value ? null : "Minimum component separation is violated.");
            }

            foreach (var obj in geometry.AllObjects.Where(o => o.NetId is not null && targetNetIds.Contains(o.NetId.Value.Value)))
            {
                var actual = _kernel.Distance(sourceBoundary, obj.Geometry);
                yield return Result(constraint, actual.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference(obj.EntityType, obj.EntityId)], required, actual, ConstraintEvidence.From(("measurement", "component-to-net-geometry")), actual.Value >= required.Value ? null : "Minimum component-to-net separation is violated.");
            }
        }
    }

    private IEnumerable<ConstraintEvaluation> InsideOutsideRegion(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, bool inside)
    {
        var regionRefs = constraint.Target is null ? resolver.Resolve(constraint.Source).Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "REGION").ToArray() : resolver.Resolve(constraint.Target).Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "REGION").ToArray();
        var regions = geometry.RegionObjects.Where(r => regionRefs.Any(rr => rr.EntityId == r.EntityId)).ToArray();
        if (regions.Length == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "region")), "A concrete region is required.");
            yield break;
        }

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source)))
        {
            var boundary = component.PlacementBoundary;
            if (boundary is null)
            {
                yield return UnknownGeometry(constraint, "COMPONENT", component.Component.Id.Value, "Component has no body or courtyard geometry.");
                continue;
            }

            var contained = regions.Any(r => _kernel.Contains(r.Geometry, boundary));
            var intersects = regions.Any(r => _kernel.Intersects(r.Geometry, boundary));
            var pass = inside ? contained : !intersects;
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("inside", contained), ("intersects", intersects)), pass ? null : inside ? "Component is not inside required region." : "Component intersects forbidden region.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumTrackWidth(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var required = new LengthUnits(LongParam(constraint.Parameters, "minimumUnits", "widthUnits") ?? fallback.Value);
        foreach (var track in SelectedTracks(project, resolver, constraint))
        {
            yield return Result(constraint, track.Width.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("TRACK_SEGMENT", track.Id.Value)], required, track.Width, ConstraintEvidence.From(("trackWidthUnits", track.Width.Value)), track.Width.Value >= required.Value ? null : "Track width is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumClearance(PhysicalGeometryModel geometry, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var required = new LengthUnits(LongParam(constraint.Parameters, "minimumUnits", "clearanceUnits") ?? fallback.Value);
        var copper = geometry.RouteObjects.Concat(geometry.CopperZoneObjects).Concat(geometry.ViaObjects).Where(o => o.NetId is not null).ToArray();
        var any = false;
        for (var i = 0; i < copper.Length; i++)
        {
            for (var j = i + 1; j < copper.Length; j++)
            {
                if (copper[i].NetId == copper[j].NetId || (copper[i].LayerId is not null && copper[j].LayerId is not null && copper[i].LayerId != copper[j].LayerId))
                {
                    continue;
                }

                var actual = _kernel.Distance(copper[i].Geometry, copper[j].Geometry);
                yield return Result(constraint, actual.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference(copper[i].EntityType, copper[i].EntityId), new EntityReference(copper[j].EntityType, copper[j].EntityId)], required, actual, ConstraintEvidence.From(("phase", "exact-clearance")), actual.Value >= required.Value ? null : "Copper clearance is below minimum.");
                any = true;
            }
        }

        if (!any)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], required, null, ConstraintEvidence.Empty, null);
        }
    }

    private IEnumerable<ConstraintEvaluation> CopperToEdge(PhysicalGeometryModel geometry, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var board = geometry.BoardObjects.FirstOrDefault(o => o.Kind == PhysicalObjectKind.Board);
        var required = new LengthUnits(LongParam(constraint.Parameters, "minimumUnits", "distanceUnits") ?? fallback.Value);
        if (board is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], required, null, ConstraintEvidence.From(("missing", "board.outline")), "Board outline is required.");
            yield break;
        }

        foreach (var obj in geometry.RouteObjects.Concat(geometry.CopperZoneObjects).Concat(geometry.ViaObjects))
        {
            var inside = _kernel.Contains(board.Geometry, obj.Geometry);
            var actual = inside ? new LengthUnits(DistanceToOuterBoundary(board.Geometry, obj.Geometry)) : new LengthUnits(0);
            var pass = inside && actual.Value >= required.Value;
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference(obj.EntityType, obj.EntityId)], required, actual, ConstraintEvidence.From(("insideBoard", inside)), pass ? null : "Copper-to-edge distance is below minimum or outside board.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumDrill(CanonicalProject project, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var required = new LengthUnits(LongParam(constraint.Parameters, "minimumUnits", "drillDiameterUnits") ?? fallback.Value);
        foreach (var via in project.PhysicalDesignState.Vias)
        {
            yield return Result(constraint, via.DrillDiameter.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], required, via.DrillDiameter, ConstraintEvidence.From(("drillDiameterUnits", via.DrillDiameter.Value)), via.DrillDiameter.Value >= required.Value ? null : "Via drill is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumViaDiameter(CanonicalProject project, EffectiveConstraint constraint, LengthUnits fallback)
    {
        var required = new LengthUnits(LongParam(constraint.Parameters, "minimumUnits", "outerDiameterUnits") ?? fallback.Value);
        foreach (var via in project.PhysicalDesignState.Vias)
        {
            yield return Result(constraint, via.OuterDiameter.Value >= required.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], required, via.OuterDiameter, ConstraintEvidence.From(("outerDiameterUnits", via.OuterDiameter.Value)), via.OuterDiameter.Value >= required.Value ? null : "Via diameter is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MaximumVias(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var max = LongParam(constraint.Parameters, "maximum", "maxVias");
        if (max is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "maxVias")), "Maximum via count parameter is required.");
            yield break;
        }

        var refs = resolver.Resolve(constraint.Source);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        foreach (var route in project.PhysicalDesignState.Routes.Where(r => netIds.Count == 0 || netIds.Contains(r.NetId.Value)))
        {
            var actual = route.ViaIds.Count;
            yield return Result(constraint, actual <= max.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("ROUTE", route.Id.Value), new EntityReference("NET", route.NetId.Value)], new LengthUnits(max.Value), new LengthUnits(actual), ConstraintEvidence.From(("viaCount", actual)), actual <= max.Value ? null : "Route uses too many vias.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MaximumLength(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var max = LongParam(constraint.Parameters, "maximumUnits", "maxLengthUnits", "lengthUnits");
        if (max is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "maximumUnits")), "Maximum length parameter is required.");
            yield break;
        }

        var refs = resolver.Resolve(constraint.Source);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        foreach (var route in project.PhysicalDesignState.Routes.Where(r => netIds.Count == 0 || netIds.Contains(r.NetId.Value)))
        {
            var actual = route.TrackSegments.Sum(TrackLength);
            yield return Result(constraint, actual <= max.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("ROUTE", route.Id.Value), new EntityReference("NET", route.NetId.Value)], new LengthUnits(max.Value), new LengthUnits(actual), ConstraintEvidence.From(("lengthUnits", actual)), actual <= max.Value ? null : "Route length exceeds maximum.");
        }
    }

    private IEnumerable<ComponentGeometry> Components(PhysicalGeometryModel geometry, IReadOnlyList<EntityReference> refs)
    {
        var ids = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "COMPONENT").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        return ids.Count == 0 ? geometry.Components : geometry.Components.Where(c => ids.Contains(c.Component.Id.Value));
    }

    private static IEnumerable<TrackSegment> SelectedTracks(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var refs = resolver.Resolve(constraint.Source);
        var trackIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "TRACKSEGMENT").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        return project.PhysicalDesignState.Routes
            .Where(r => netIds.Count == 0 || netIds.Contains(r.NetId.Value))
            .SelectMany(r => r.TrackSegments)
            .Where(t => trackIds.Count == 0 || trackIds.Contains(t.Id.Value));
    }

    private ConstraintEvaluation UnknownSelector(EffectiveConstraint constraint) =>
        Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "selector target")), "Constraint selector resolved no target.");

    private ConstraintEvaluation UnknownGeometry(EffectiveConstraint constraint, string entityType, string entityId, string message) =>
        Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference(entityType, entityId)], null, null, ConstraintEvidence.From(("missing", "geometry")), message);

    private ConstraintEvaluation Result(EffectiveConstraint constraint, ConstraintEvaluationStatus status, IReadOnlyList<EntityReference> refs, LengthUnits? required, LengthUnits? actual, ConstraintEvidence evidence, string? message) =>
        new(constraint.Id, constraint.Type, constraint.Enforcement, status, refs, required, actual, evidence, constraint.Provenance, message);

    private static IEnumerable<PhysicalFinding> Findings(IEnumerable<ConstraintEvaluation> evaluations, IEnumerable<ConstraintConflict> conflicts)
    {
        foreach (var conflict in conflicts)
        {
            yield return new PhysicalFinding($"finding:conflict:{conflict.FirstConstraintId.Value}:{conflict.SecondConstraintId.Value}", "ERROR", "ConstraintConflict", conflict.Message, [new EntityReference("CONSTRAINT", conflict.FirstConstraintId.Value), new EntityReference("CONSTRAINT", conflict.SecondConstraintId.Value)], conflict.Evidence, Plan02DiagnosticCodes.ConstraintConflict, "OPEN");
        }

        foreach (var evaluation in evaluations.Where(e => e.Status is ConstraintEvaluationStatus.Fail or ConstraintEvaluationStatus.Unknown))
        {
            var code = evaluation.Status == ConstraintEvaluationStatus.Fail ? Plan02DiagnosticCodes.ConstraintFailed : Plan02DiagnosticCodes.ConstraintUnknown;
            yield return new PhysicalFinding($"finding:{evaluation.ConstraintId.Value}:{evaluation.Status}", evaluation.BlocksCandidate ? "ERROR" : "WARNING", "ConstraintEvaluation", evaluation.Message ?? $"{evaluation.ConstraintType} evaluated as {evaluation.Status}.", evaluation.AffectedEntities, evaluation.Evidence, code, "OPEN");
        }
    }

    private static long? LongParam(JsonElement parameters, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parameters.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static decimal Normalize(decimal degrees)
    {
        var value = degrees % 360;
        return value < 0 ? value + 360 : value;
    }

    private static long TrackLength(TrackSegment track)
    {
        var dx = track.End.X.Value - track.Start.X.Value;
        var dy = track.End.Y.Value - track.Start.Y.Value;
        return (long)Math.Ceiling(Math.Sqrt((double)dx * dx + (double)dy * dy));
    }

    private static long DistanceToOuterBoundary(GeometryPolygon container, GeometryPolygon candidate)
    {
        var min = double.PositiveInfinity;
        var containerEdges = RingEdges(container.Outer).ToArray();
        var candidateEdges = RingEdges(candidate.Outer).ToArray();
        foreach (var point in candidate.Outer)
        {
            foreach (var edge in containerEdges)
            {
                min = Math.Min(min, PointSegmentDistanceSquared(point, edge));
            }
        }

        foreach (var point in container.Outer)
        {
            foreach (var edge in candidateEdges)
            {
                min = Math.Min(min, PointSegmentDistanceSquared(point, edge));
            }
        }

        return (long)Math.Ceiling(Math.Sqrt(min));
    }

    private static IEnumerable<GeometrySegment> RingEdges(IReadOnlyList<GeometryPoint> ring)
    {
        for (var i = 0; i < ring.Count; i++)
        {
            yield return new GeometrySegment(ring[i], ring[(i + 1) % ring.Count]);
        }
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
