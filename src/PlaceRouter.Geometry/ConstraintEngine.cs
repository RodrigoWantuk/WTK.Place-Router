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

public sealed record EffectiveConstraintSet(
    IReadOnlyList<EffectiveConstraint> Constraints,
    IReadOnlyList<ConstraintConflict> Conflicts)
{
    public EffectiveConstraint? EffectiveFor(EntityReference entity, string type, ConstraintSelectorResolver resolver) =>
        EffectiveFor(entity, null, new ConstraintScope([], null, null, []), type, resolver);

    public EffectiveConstraint? EffectiveFor(EntityReference source, EntityReference? target, ConstraintScope scope, string type, ConstraintSelectorResolver resolver) =>
        Merge(
            Constraints
                .Where(c => string.Equals(ConstraintSelectorResolver.Canon(c.Type), ConstraintSelectorResolver.Canon(type), StringComparison.Ordinal))
                .Where(c => Matches(c.Source, source, resolver))
                .Where(c => c.Target is null || (target is not null && Matches(c.Target, target, resolver)))
                .Where(c => ScopeApplies(c.Scope, scope))
                .ToArray(),
            source,
            target,
            scope,
            type);

    public IReadOnlyList<EffectiveConstraint> ConstraintsForEvaluation(CanonicalProject project, ConstraintSelectorResolver resolver)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal)
        {
            "MINIMUMTRACKWIDTH",
            "MINIMUMWIDTH"
        };
        var result = Constraints
            .Where(c => !handled.Contains(ConstraintSelectorResolver.Canon(c.Type)))
            .ToList();

        foreach (var route in project.PhysicalDesignState.Routes)
        {
            foreach (var track in route.TrackSegments)
            {
                var trackScope = new ConstraintScope([track.LayerId], null, null, ["TRACK"]);
                var applicable = Constraints
                    .Where(c => ConstraintSelectorResolver.Canon(c.Type) is "MINIMUMTRACKWIDTH" or "MINIMUMWIDTH")
                    .Where(c => TrackMatches(c.Source, route, track, resolver))
                    .Where(c => ScopeApplies(c.Scope, trackScope))
                    .ToArray();
                foreach (var group in applicable.GroupBy(c => EnforcementClass(c.Enforcement), StringComparer.Ordinal))
                {
                    var merged = Merge(group.ToArray(), new EntityReference("TRACK_SEGMENT", track.Id.Value), null, trackScope, "MinimumTrackWidth");
                    if (merged is not null)
                    {
                        result.Add(merged);
                    }
                }
            }
        }

        return result;
    }

    private static EffectiveConstraint? Merge(IReadOnlyList<EffectiveConstraint> constraints, EntityReference source, EntityReference? target, ConstraintScope scope, string type)
    {
        if (constraints.Count == 0)
        {
            return null;
        }

        var canonType = ConstraintSelectorResolver.Canon(type);
        if (canonType is "MINIMUMTRACKWIDTH" or "MINIMUMWIDTH" or "MINIMUMCLEARANCE" or "MINIMUMDRILL" or "MINIMUMVIADIAMETER" or "MINIMUMANNULARRING" or "MINIMUMCOMPONENTSPACING")
        {
            return MergeMinimum(constraints, source, target, scope, type, "minimumUnits", "widthUnits", "clearanceUnits", "drillDiameterUnits", "outerDiameterUnits", "annularRingUnits", "distanceUnits");
        }

        if (canonType is "MAXIMUMVIAS" or "MAXIMUMLENGTH")
        {
            return MergeMaximum(constraints, source, target, scope, type, "maximum", "maxVias", "maximumUnits", "maxLengthUnits", "lengthUnits");
        }

        if (canonType is "ALLOWEDROTATION" or "ALLOWEDROTATIONS")
        {
            return MergeAllowedNumbers(constraints, source, target, scope, type, "allowedDegrees");
        }

        if (canonType is "ALLOWEDSIDE")
        {
            return MergeAllowedStrings(constraints, source, target, scope, type, "allowedSides");
        }

        if (canonType is "ALLOWEDVIATYPES")
        {
            return MergeAllowedStrings(constraints, source, target, scope, type, "allowedViaTypes");
        }

        return constraints
            .OrderByDescending(c => c.Specificity)
            .ThenByDescending(c => c.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private static EffectiveConstraint MergeMinimum(IReadOnlyList<EffectiveConstraint> constraints, EntityReference source, EntityReference? target, ConstraintScope scope, string type, params string[] names)
    {
        var required = constraints.Where(IsRequired).ToArray();
        var candidates = required.Length == 0 ? constraints : required;
        var unknown = candidates.FirstOrDefault(c => HasUnknownParameter(c.Parameters));
        if (unknown is not null)
        {
            return Merged(unknown, source, target, scope, type, JsonDocument.Parse("""{"status":"UNKNOWN"}""").RootElement.Clone(), candidates);
        }

        var values = candidates
            .Select(c => (Constraint: c, Value: LongParam(c.Parameters, names)))
            .Where(v => v.Value is not null)
            .ToArray();
        if (values.Length == 0)
        {
            var winner = candidates.OrderByDescending(c => c.Specificity).First();
            return Merged(winner, source, target, scope, type, JsonDocument.Parse("""{"status":"UNKNOWN"}""").RootElement.Clone(), candidates);
        }

        var selected = values.OrderByDescending(v => v.Value!.Value).ThenByDescending(v => v.Constraint.Specificity).First();
        return Merged(selected.Constraint, source, target, scope, type, Param(names[0], selected.Value!.Value), candidates);
    }

    private static EffectiveConstraint MergeMaximum(IReadOnlyList<EffectiveConstraint> constraints, EntityReference source, EntityReference? target, ConstraintScope scope, string type, params string[] names)
    {
        var required = constraints.Where(IsRequired).ToArray();
        var candidates = required.Length == 0 ? constraints : required;
        var unknown = candidates.FirstOrDefault(c => HasUnknownParameter(c.Parameters));
        if (unknown is not null)
        {
            return Merged(unknown, source, target, scope, type, JsonDocument.Parse("""{"status":"UNKNOWN"}""").RootElement.Clone(), candidates);
        }

        var values = candidates
            .Select(c => (Constraint: c, Value: LongParam(c.Parameters, names)))
            .Where(v => v.Value is not null)
            .ToArray();
        if (values.Length == 0)
        {
            var winner = candidates.OrderByDescending(c => c.Specificity).First();
            return Merged(winner, source, target, scope, type, JsonDocument.Parse("""{"status":"UNKNOWN"}""").RootElement.Clone(), candidates);
        }

        var selected = values.OrderBy(v => v.Value!.Value).ThenByDescending(v => v.Constraint.Specificity).First();
        return Merged(selected.Constraint, source, target, scope, type, Param(names[0], selected.Value!.Value), candidates);
    }

    private static EffectiveConstraint MergeAllowedNumbers(IReadOnlyList<EffectiveConstraint> constraints, EntityReference source, EntityReference? target, ConstraintScope scope, string type, string key)
    {
        var required = constraints.Where(IsRequired).ToArray();
        var candidates = required.Length == 0 ? constraints : required;
        var sets = candidates.Select(c => EffectiveConstraintResolver.NumericSet(c.Parameters, key, "rotations")).Where(s => s.Count > 0).ToArray();
        var merged = sets.Length == 0 ? new HashSet<decimal>() : sets.Skip(1).Aggregate(sets[0], (current, next) => current.Intersect(next).ToHashSet());
        var winner = candidates.OrderByDescending(c => c.Specificity).First();
        return Merged(winner, source, target, scope, type, ParamArray(key, merged), candidates);
    }

    private static EffectiveConstraint MergeAllowedStrings(IReadOnlyList<EffectiveConstraint> constraints, EntityReference source, EntityReference? target, ConstraintScope scope, string type, string key)
    {
        var required = constraints.Where(IsRequired).ToArray();
        var candidates = required.Length == 0 ? constraints : required;
        var sets = candidates.Select(c => EffectiveConstraintResolver.StringSet(c.Parameters, key, "sides", "side", "viaTypes")).Where(s => s.Count > 0).ToArray();
        var merged = sets.Length == 0 ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : sets.Skip(1).Aggregate(sets[0], (current, next) => current.Intersect(next, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var winner = candidates.OrderByDescending(c => c.Specificity).First();
        return Merged(winner, source, target, scope, type, ParamArray(key, merged), candidates);
    }

    private static EffectiveConstraint Merged(EffectiveConstraint winner, EntityReference source, EntityReference? target, ConstraintScope scope, string type, JsonElement parameters, IReadOnlyList<EffectiveConstraint> mergedFrom) =>
        new(
            winner.Id,
            type,
            new ConstraintSelector("ENTITY", source.EntityType, [source.EntityId], null),
            target is null ? null : new ConstraintSelector("ENTITY", target.EntityType, [target.EntityId], null),
            parameters,
            mergedFrom.Any(IsRequired) ? "REQUIRED" : winner.Enforcement,
            scope,
            winner.Provenance,
            string.Join("+", mergedFrom.Select(c => c.Id.Value).Order(StringComparer.Ordinal)),
            mergedFrom.Max(c => c.Specificity));

    private static bool Matches(ConstraintSelector selector, EntityReference entity, ConstraintSelectorResolver resolver) =>
        ConstraintSelectorResolver.Canon(selector.Kind) == "ALL" ||
        resolver.Resolve(selector).Any(r => SameEntity(r, entity));

    private static bool TrackMatches(ConstraintSelector selector, Route route, TrackSegment track, ConstraintSelectorResolver resolver)
    {
        if (ConstraintSelectorResolver.Canon(selector.Kind) == "ALL")
        {
            return true;
        }

        var refs = resolver.Resolve(selector);
        return refs.Any(r =>
            SameEntity(r, new EntityReference("TRACK_SEGMENT", track.Id.Value)) ||
            SameEntity(r, new EntityReference("ROUTE", route.Id.Value)) ||
            SameEntity(r, new EntityReference("NET", route.NetId.Value)));
    }

    private static bool ScopeApplies(ConstraintScope constraintScope, ConstraintScope requestedScope)
    {
        var constraintLayers = constraintScope.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        var requestedLayers = requestedScope.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        if (constraintLayers.Count > 0 && requestedLayers.Count > 0 && !constraintLayers.Overlaps(requestedLayers))
        {
            return false;
        }

        var constraintTypes = constraintScope.GeometryTypes.Select(ConstraintSelectorResolver.Canon).ToHashSet(StringComparer.Ordinal);
        var requestedTypes = requestedScope.GeometryTypes.Select(ConstraintSelectorResolver.Canon).ToHashSet(StringComparer.Ordinal);
        return constraintTypes.Count == 0 || requestedTypes.Count == 0 || constraintTypes.Overlaps(requestedTypes);
    }

    private static bool IsRequired(EffectiveConstraint constraint) =>
        string.Equals(constraint.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase);

    private static string EnforcementClass(string enforcement) =>
        ConstraintSelectorResolver.Canon(enforcement) switch
        {
            "REQUIRED" => "REQUIRED",
            "PREFERRED" => "PREFERRED",
            "OPTIMIZATIONGOAL" => "OPTIMIZATION_GOAL",
            _ => enforcement.ToUpperInvariant()
        };

    private static bool HasUnknownParameter(JsonElement parameters) =>
        parameters.TryGetProperty("status", out var status) &&
        status.ValueKind == JsonValueKind.String &&
        string.Equals(status.GetString(), "UNKNOWN", StringComparison.OrdinalIgnoreCase);

    private static long? LongParam(JsonElement parameters, params string[] names)
    {
        foreach (var name in names)
        {
            if (parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static JsonElement Param(string key, long value) =>
        JsonDocument.Parse($$"""{"{{key}}":{{value}}}""").RootElement.Clone();

    private static JsonElement ParamArray(string key, IEnumerable<decimal> values) =>
        JsonDocument.Parse($$"""{"{{key}}":[{{string.Join(",", values)}}]}""").RootElement.Clone();

    private static JsonElement ParamArray(string key, IEnumerable<string> values) =>
        JsonDocument.Parse($$"""{"{{key}}":[{{string.Join(",", values.Select(v => "\"" + v + "\""))}}]}""").RootElement.Clone();

    private static bool SameEntity(EntityReference left, EntityReference right) =>
        string.Equals(ConstraintSelectorResolver.Canon(left.EntityType), ConstraintSelectorResolver.Canon(right.EntityType), StringComparison.Ordinal) &&
        string.Equals(left.EntityId, right.EntityId, StringComparison.Ordinal);
}

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
    public EffectiveConstraintSet Resolve(CanonicalProject project, ManufacturingRules manufacturingRules)
    {
        var constraints = new List<EffectiveConstraint>();
        constraints.AddRange(ManufacturingConstraints(project, manufacturingRules));
        constraints.AddRange(project.Constraints.Where(c => c.Enabled).Select(c => new EffectiveConstraint(c.Id, c.Type, c.Source, c.Target, c.Parameters, c.Enforcement, c.Scope, c.Provenance, c.Provenance.Kind, Specificity(c.Source) + (c.Target is null ? 0 : Specificity(c.Target)))));

        return new EffectiveConstraintSet(constraints, DetectConflicts(constraints, new ConstraintSelectorResolver(project)));
    }

    private static IEnumerable<EffectiveConstraint> ManufacturingConstraints(CanonicalProject project, ManufacturingRules rules)
    {
        var scope = new ConstraintScope([], null, null, []);
        var source = new ConstraintSelector("ALL", null, [], null);
        yield return Mfg("mfg_min_track_width", "MinimumTrackWidth", source, Param("minimumUnits", rules.MinimumTrackWidth), rules.MinimumTrackWidth.Provenance, scope);
        yield return Mfg("mfg_min_clearance", "MinimumClearance", source, Param("minimumUnits", rules.MinimumClearance), rules.MinimumClearance.Provenance, scope);
        yield return Mfg("mfg_copper_to_edge", "CopperToEdge", source, Param("minimumUnits", rules.CopperToEdge), rules.CopperToEdge.Provenance, scope);
        yield return Mfg("mfg_min_drill", "MinimumDrill", source, Param("minimumUnits", rules.MinimumDrill), rules.MinimumDrill.Provenance, scope);
        yield return Mfg("mfg_min_via_diameter", "MinimumViaDiameter", source, Param("minimumUnits", rules.MinimumViaDiameter), rules.MinimumViaDiameter.Provenance, scope);
        yield return Mfg("mfg_annular_ring", "MinimumAnnularRing", source, Param("minimumUnits", rules.AnnularRing), rules.AnnularRing.Provenance, scope);
        yield return Mfg("mfg_min_component_spacing", "MinimumComponentSpacing", new ConstraintSelector("ALL", "COMPONENT", [], null), Param("distanceUnits", rules.MinimumComponentSpacing), rules.MinimumComponentSpacing.Provenance, scope);
        yield return Mfg("mfg_allowed_via_types", "AllowedViaTypes", source, Param("allowedViaTypes", rules.AllowedViaTypes), rules.AllowedViaTypes.Provenance, scope);
        yield return Mfg("mfg_layer_compatibility", "LayerCompatibility", source, Param("allowedLayerCounts", rules.AllowedLayerCounts), rules.AllowedLayerCounts.Provenance, scope);

        static EffectiveConstraint Mfg(string id, string type, ConstraintSelector source, JsonElement parameters, Provenance provenance, ConstraintScope scope) =>
            new(new ConstraintId(id), type, source, null, parameters, "REQUIRED", scope, provenance, "ManufacturingProfile", 1);
    }

    private static IReadOnlyList<ConstraintConflict> DetectConflicts(IReadOnlyList<EffectiveConstraint> constraints, ConstraintSelectorResolver resolver)
    {
        var conflicts = new List<ConstraintConflict>();
        var required = constraints.Where(c => string.Equals(c.Enforcement, "REQUIRED", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var pair in required.SelectMany((a, i) => required.Skip(i + 1).Select(b => (a, b))))
        {
            if (!SelectorsOverlap(pair.a.Source, pair.b.Source, resolver) || !ScopesOverlap(pair.a.Scope, pair.b.Scope))
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

            if (type == "ALLOWEDVIATYPES")
            {
                var left = StringSet(pair.a.Parameters, "allowedViaTypes", "viaTypes");
                var right = StringSet(pair.b.Parameters, "allowedViaTypes", "viaTypes");
                if (left.Count > 0 && right.Count > 0 && !left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any())
                {
                    conflicts.Add(new ConstraintConflict(pair.a.Id, pair.b.Id, "Required allowed via type constraints have no common value.", ConstraintEvidence.From(("left", string.Join(",", left)), ("right", string.Join(",", right)))));
                }
            }
        }

        return conflicts;
    }

    private static bool SelectorsOverlap(ConstraintSelector left, ConstraintSelector right, ConstraintSelectorResolver resolver)
    {
        if (ConstraintSelectorResolver.Canon(left.Kind) == "ALL" || ConstraintSelectorResolver.Canon(right.Kind) == "ALL")
        {
            return true;
        }

        var leftRefs = resolver.Resolve(left);
        var rightRefs = resolver.Resolve(right);
        return leftRefs.Any(l => rightRefs.Any(r =>
            string.Equals(ConstraintSelectorResolver.Canon(l.EntityType), ConstraintSelectorResolver.Canon(r.EntityType), StringComparison.Ordinal) &&
            string.Equals(l.EntityId, r.EntityId, StringComparison.Ordinal)));
    }

    private static bool ScopesOverlap(ConstraintScope left, ConstraintScope right)
    {
        var leftLayers = left.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        var rightLayers = right.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        if (leftLayers.Count > 0 && rightLayers.Count > 0 && !leftLayers.Overlaps(rightLayers))
        {
            return false;
        }

        var leftTypes = left.GeometryTypes.Select(ConstraintSelectorResolver.Canon).ToHashSet(StringComparer.Ordinal);
        var rightTypes = right.GeometryTypes.Select(ConstraintSelectorResolver.Canon).ToHashSet(StringComparer.Ordinal);
        return leftTypes.Count == 0 || rightTypes.Count == 0 || leftTypes.Overlaps(rightTypes);
    }

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

    internal static JsonElement Param(string key, ManufacturingLengthRule rule) =>
        rule.HasKnownValue
            ? JsonDocument.Parse($$"""{"{{key}}":{{rule.Value!.Value.Value}},"status":"{{rule.Status}}"}""").RootElement.Clone()
            : JsonDocument.Parse($$"""{"status":"UNKNOWN","missingCapability":"{{rule.Name}}"}""").RootElement.Clone();

    internal static JsonElement Param(string key, ManufacturingSetRule rule)
    {
        if (!rule.HasKnownValue)
        {
            return JsonDocument.Parse($$"""{"status":"UNKNOWN","missingCapability":"{{rule.Name}}"}""").RootElement.Clone();
        }

        var values = string.Join(",", rule.Values!.Select(v => "\"" + v + "\""));
        return JsonDocument.Parse($$"""{"{{key}}":[{{values}}],"status":"{{rule.Status}}"}""").RootElement.Clone();
    }

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
        var effectiveSet = _resolver.Resolve(project, rules);
        var resolver = new ConstraintSelectorResolver(project);
        var evaluations = effectiveSet.ConstraintsForEvaluation(project, resolver).SelectMany(c => EvaluateConstraint(project, geometry, resolver, c, rules)).ToArray();
        var readiness = WithRequiredUnknowns(_readinessAnalyzer.Analyze(project, geometry), evaluations);
        var findings = Findings(evaluations, effectiveSet.Conflicts).ToArray();
        return new ConstraintEvaluationReport(evaluations, effectiveSet.Conflicts, readiness, findings);
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
            "MINIMUMSEPARATION" => MinimumSeparation(geometry, resolver, constraint, rules.MinimumComponentSpacing.Value),
            "MINIMUMCOMPONENTSPACING" => MinimumSeparation(geometry, resolver, constraint, rules.MinimumComponentSpacing.Value),
            "INSIDEREGION" => InsideOutsideRegion(geometry, resolver, constraint, inside: true),
            "OUTSIDEREGION" => InsideOutsideRegion(geometry, resolver, constraint, inside: false),
            "MINIMUMTRACKWIDTH" or "MINIMUMWIDTH" => MinimumTrackWidth(project, resolver, constraint, rules.MinimumTrackWidth.Value),
            "MINIMUMCLEARANCE" => MinimumClearance(geometry, resolver, constraint, rules.MinimumClearance.Value),
            "COPPERTOEDGE" => CopperToEdge(geometry, resolver, constraint, rules.CopperToEdge.Value),
            "MINIMUMDRILL" => MinimumDrill(project, resolver, constraint, rules.MinimumDrill.Value),
            "MINIMUMVIADIAMETER" => MinimumViaDiameter(project, resolver, constraint, rules.MinimumViaDiameter.Value),
            "MINIMUMANNULARRING" => MinimumAnnularRing(project, resolver, constraint, rules.AnnularRing.Value),
            "ALLOWEDVIATYPES" => AllowedViaTypes(project, resolver, constraint, rules.AllowedViaTypes.Values),
            "LAYERCOMPATIBILITY" => LayerCompatibility(project, constraint, rules.AllowedLayerCounts.Values),
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

        var components = Components(geometry, resolver.Resolve(constraint.Source), constraint.Source).ToArray();
        if (components.Length == 0)
        {
            yield return UnknownSelector(constraint);
            yield break;
        }

        foreach (var component in components)
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
        var components = Components(geometry, resolver.Resolve(constraint.Source), constraint.Source).ToArray();
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
        var sourceObjects = PhysicalObjects(geometry, resolver.Resolve(constraint.Source), constraint.Source, constraint.Scope).ToArray();
        if (sourceObjects.Length == 0)
        {
            yield return UnknownSelector(constraint);
            yield break;
        }

        foreach (var obj in sourceObjects)
        {
            var hit = geometry.KeepoutObjects.FirstOrDefault(k => AppliesTo(k, obj.Kind) && SameLayer(k, obj) && _kernel.Intersects(obj.Geometry, k.Geometry));
            yield return Result(constraint, hit is null ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference(obj.EntityType, obj.EntityId)], null, null, ConstraintEvidence.From(("keepout", hit?.Id), ("objectKind", obj.Kind.ToString())), hit is null ? null : "Physical object intersects keepout.");
        }
    }

    private IEnumerable<ConstraintEvaluation> FixedPosition(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source), constraint.Source))
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

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source), constraint.Source))
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

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source), constraint.Source))
        {
            var pass = allowed.Contains(component.Pose.Side);
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", component.Component.Id.Value)], null, null, ConstraintEvidence.From(("actualSide", component.Pose.Side), ("allowedSides", string.Join(",", allowed))), pass ? null : "Component side is not allowed.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumSeparation(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "distanceUnits", "minimumUnits", "clearanceUnits");
        var sources = Components(geometry, resolver.Resolve(constraint.Source), constraint.Source).ToArray();
        if (sources.Length == 0)
        {
            yield return UnknownSelector(constraint);
            yield break;
        }

        var targetRefs = constraint.Target is null ? [] : resolver.Resolve(constraint.Target);
        var targets = constraint.Target is null ? sources.Skip(1).ToArray() : Components(geometry, targetRefs, constraint.Target).ToArray();
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

                if (required is null)
                {
                    yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference("COMPONENT", target.Component.Id.Value)], null, null, ConstraintEvidence.From(("missing", "minimum separation")), "Minimum separation value is required.");
                    continue;
                }

                var actual = _kernel.Distance(sourceBoundary, targetBoundary);
                yield return Result(constraint, actual.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference("COMPONENT", target.Component.Id.Value)], required, actual, ConstraintEvidence.From(("measurement", "nearest-geometry")), actual.Value >= required.Value.Value ? null : "Minimum component separation is violated.");
            }

            foreach (var obj in geometry.AllObjects.Where(o => o.NetId is not null && targetNetIds.Contains(o.NetId.Value.Value)))
            {
                if (required is null)
                {
                    yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference(obj.EntityType, obj.EntityId)], null, null, ConstraintEvidence.From(("missing", "minimum separation")), "Minimum separation value is required.");
                    continue;
                }

                var actual = _kernel.Distance(sourceBoundary, obj.Geometry);
                yield return Result(constraint, actual.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("COMPONENT", source.Component.Id.Value), new EntityReference(obj.EntityType, obj.EntityId)], required, actual, ConstraintEvidence.From(("measurement", "component-to-net-geometry")), actual.Value >= required.Value.Value ? null : "Minimum component-to-net separation is violated.");
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

        foreach (var component in Components(geometry, resolver.Resolve(constraint.Source), constraint.Source))
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

    private IEnumerable<ConstraintEvaluation> MinimumTrackWidth(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "widthUnits");
        var tracks = SelectedTracks(project, resolver, constraint).ToArray();
        if (tracks.Length == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], required, null, ConstraintEvidence.Empty, null);
            yield break;
        }

        foreach (var track in tracks)
        {
            if (required is null)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("TRACK_SEGMENT", track.Id.Value)], null, track.Width, ConstraintEvidence.From(("missing", "minimum track width")), "Minimum track width is required.");
                continue;
            }

            yield return Result(constraint, track.Width.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("TRACK_SEGMENT", track.Id.Value)], required, track.Width, ConstraintEvidence.From(("trackWidthUnits", track.Width.Value)), track.Width.Value >= required.Value.Value ? null : "Track width is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumClearance(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "clearanceUnits");
        var source = CopperObjects(geometry, resolver.Resolve(constraint.Source), constraint.Source, constraint.Scope).ToArray();
        var target = constraint.Target is null ? CopperObjects(geometry, [], new ConstraintSelector("ALL", null, [], null), constraint.Scope).ToArray() : CopperObjects(geometry, resolver.Resolve(constraint.Target), constraint.Target, constraint.Scope).ToArray();
        var any = false;
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in source)
        {
            foreach (var b in target)
            {
                if (ReferenceEquals(a, b) || a.Id == b.Id || a.NetId == b.NetId || !SameLayer(a, b))
                {
                    continue;
                }

                if (!seenPairs.Add(PairKey(a, b)))
                {
                    continue;
                }

                if (required is null)
                {
                    yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference(a.EntityType, a.EntityId), new EntityReference(b.EntityType, b.EntityId)], null, null, ConstraintEvidence.From(("missing", "minimum clearance")), "Minimum clearance is required.");
                    any = true;
                    continue;
                }

                var actual = _kernel.Distance(a.Geometry, b.Geometry);
                yield return Result(constraint, actual.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference(a.EntityType, a.EntityId), new EntityReference(b.EntityType, b.EntityId)], required, actual, ConstraintEvidence.From(("phase", "exact-clearance")), actual.Value >= required.Value.Value ? null : "Copper clearance is below minimum.");
                any = true;
            }
        }

        if (!any)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], required, null, ConstraintEvidence.Empty, null);
        }
    }

    private IEnumerable<ConstraintEvaluation> CopperToEdge(PhysicalGeometryModel geometry, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var board = geometry.BoardObjects.FirstOrDefault(o => o.Kind == PhysicalObjectKind.Board);
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "distanceUnits");
        if (board is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], required, null, ConstraintEvidence.From(("missing", "board.outline")), "Board outline is required.");
            yield break;
        }

        var objects = CopperObjects(geometry, resolver.Resolve(constraint.Source), constraint.Source, constraint.Scope).ToArray();
        if (objects.Length == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], required, null, ConstraintEvidence.Empty, null);
            yield break;
        }

        foreach (var obj in objects)
        {
            if (required is null)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference(obj.EntityType, obj.EntityId)], null, null, ConstraintEvidence.From(("missing", "copper-to-edge")), "Copper-to-edge minimum is required.");
                continue;
            }

            var inside = _kernel.Contains(board.Geometry, obj.Geometry);
            var actual = inside ? new LengthUnits(DistanceToBoardEdges(board.Geometry, obj.Geometry)) : new LengthUnits(0);
            var pass = inside && actual.Value >= required.Value.Value;
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference(obj.EntityType, obj.EntityId)], required, actual, ConstraintEvidence.From(("insideBoard", inside)), pass ? null : "Copper-to-edge distance is below minimum or outside board.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumDrill(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "drillDiameterUnits");
        var vias = SelectedVias(project, resolver, constraint).ToArray();
        if (vias.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var via in vias)
        {
            if (required is null)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("VIA", via.Id.Value)], null, via.DrillDiameter, ConstraintEvidence.From(("missing", "minimum drill")), "Minimum drill is required.");
                continue;
            }

            yield return Result(constraint, via.DrillDiameter.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], required, via.DrillDiameter, ConstraintEvidence.From(("drillDiameterUnits", via.DrillDiameter.Value)), via.DrillDiameter.Value >= required.Value.Value ? null : "Via drill is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumViaDiameter(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "outerDiameterUnits");
        var vias = SelectedVias(project, resolver, constraint).ToArray();
        if (vias.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var via in vias)
        {
            if (required is null)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("VIA", via.Id.Value)], null, via.OuterDiameter, ConstraintEvidence.From(("missing", "minimum via diameter")), "Minimum via diameter is required.");
                continue;
            }

            yield return Result(constraint, via.OuterDiameter.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], required, via.OuterDiameter, ConstraintEvidence.From(("outerDiameterUnits", via.OuterDiameter.Value)), via.OuterDiameter.Value >= required.Value.Value ? null : "Via diameter is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> MinimumAnnularRing(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, LengthUnits? fallback)
    {
        var required = LengthParam(constraint.Parameters, fallback, "minimumUnits", "annularRingUnits");
        var vias = SelectedVias(project, resolver, constraint).ToArray();
        if (vias.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var via in vias)
        {
            var actual = new LengthUnits(Math.Max(0, (via.OuterDiameter.Value - via.DrillDiameter.Value) / 2));
            if (required is null)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("VIA", via.Id.Value)], null, actual, ConstraintEvidence.From(("missing", "minimum annular ring")), "Minimum annular ring is required.");
                continue;
            }

            yield return Result(constraint, actual.Value >= required.Value.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], required, actual, ConstraintEvidence.From(("annularRingUnits", actual.Value)), actual.Value >= required.Value.Value ? null : "Via annular ring is below manufacturing minimum.");
        }
    }

    private IEnumerable<ConstraintEvaluation> AllowedViaTypes(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint, IReadOnlySet<string>? fallback)
    {
        var allowed = EffectiveConstraintResolver.StringSet(constraint.Parameters, "allowedViaTypes", "viaTypes");
        if (allowed.Count == 0 && fallback is not null)
        {
            allowed = fallback;
        }

        var vias = SelectedVias(project, resolver, constraint).ToArray();
        if (vias.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var via in vias)
        {
            if (allowed.Count == 0)
            {
                yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("VIA", via.Id.Value)], null, null, ConstraintEvidence.From(("missing", "allowed via types")), "Allowed via types are required.");
                continue;
            }

            var pass = allowed.Contains(via.ViaType);
            yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("VIA", via.Id.Value)], null, null, ConstraintEvidence.From(("viaType", via.ViaType), ("allowedViaTypes", string.Join(",", allowed))), pass ? null : "Via type is not allowed by manufacturing profile.");
        }
    }

    private IEnumerable<ConstraintEvaluation> LayerCompatibility(CanonicalProject project, EffectiveConstraint constraint, IReadOnlySet<string>? fallback)
    {
        var allowed = EffectiveConstraintResolver.StringSet(constraint.Parameters, "allowedLayerCounts", "allowedCopperLayerCounts");
        if (allowed.Count == 0 && fallback is not null)
        {
            allowed = fallback;
        }

        if (allowed.Count == 0)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.NotApplicable, [new EntityReference("BOARD", "BOARD")], null, null, ConstraintEvidence.From(("missing", "allowed layer counts")), "No layer count compatibility rule is declared.");
            yield break;
        }

        var copperLayers = project.Board.Layers.Count(l => l.IsCopperCapable);
        var pass = allowed.Contains(copperLayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return Result(constraint, pass ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("BOARD", "BOARD")], null, new LengthUnits(copperLayers), ConstraintEvidence.From(("copperLayerCount", copperLayers), ("allowedLayerCounts", string.Join(",", allowed))), pass ? null : "Board copper layer count is not compatible with manufacturing profile.");
    }

    private IEnumerable<ConstraintEvaluation> MaximumVias(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var max = LongParam(constraint.Parameters, "maximum", "maxVias");
        if (max is null)
        {
            yield return Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "maxVias")), "Maximum via count parameter is required.");
            yield break;
        }

        var routes = SelectedRoutes(project, resolver, constraint).ToArray();
        if (routes.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var route in routes)
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

        var routes = SelectedRoutes(project, resolver, constraint).ToArray();
        if (routes.Length == 0)
        {
            yield return EmptySelectionResult(constraint);
            yield break;
        }

        foreach (var route in routes)
        {
            var actual = route.TrackSegments.Sum(TrackLength);
            yield return Result(constraint, actual <= max.Value ? ConstraintEvaluationStatus.Pass : ConstraintEvaluationStatus.Fail, [new EntityReference("ROUTE", route.Id.Value), new EntityReference("NET", route.NetId.Value)], new LengthUnits(max.Value), new LengthUnits(actual), ConstraintEvidence.From(("lengthUnits", actual)), actual <= max.Value ? null : "Route length exceeds maximum.");
        }
    }

    private IEnumerable<ComponentGeometry> Components(PhysicalGeometryModel geometry, IReadOnlyList<EntityReference> refs, ConstraintSelector selector)
    {
        var ids = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "COMPONENT").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0 && ConstraintSelectorResolver.Canon(selector.Kind) != "ALL")
        {
            return [];
        }

        return ids.Count == 0 ? geometry.Components : geometry.Components.Where(c => ids.Contains(c.Component.Id.Value));
    }

    private static IEnumerable<TrackSegment> SelectedTracks(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var refs = resolver.Resolve(constraint.Source);
        var trackIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "TRACKSEGMENT").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        if (trackIds.Count == 0 && netIds.Count == 0 && ConstraintSelectorResolver.Canon(constraint.Source.Kind) != "ALL")
        {
            return [];
        }

        return project.PhysicalDesignState.Routes
            .Where(r => netIds.Count == 0 || netIds.Contains(r.NetId.Value))
            .SelectMany(r => r.TrackSegments)
            .Where(t => trackIds.Count == 0 || trackIds.Contains(t.Id.Value));
    }

    private static IEnumerable<Route> SelectedRoutes(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        if (ConstraintSelectorResolver.Canon(constraint.Source.Kind) == "ALL")
        {
            return project.PhysicalDesignState.Routes;
        }

        var refs = resolver.Resolve(constraint.Source);
        var routeIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "ROUTE").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        if (routeIds.Count == 0 && netIds.Count == 0)
        {
            return [];
        }

        return project.PhysicalDesignState.Routes
            .Where(r => routeIds.Contains(r.Id.Value) || netIds.Contains(r.NetId.Value));
    }

    private static IEnumerable<Via> SelectedVias(CanonicalProject project, ConstraintSelectorResolver resolver, EffectiveConstraint constraint)
    {
        var selected = ConstraintSelectorResolver.Canon(constraint.Source.Kind) == "ALL"
            ? project.PhysicalDesignState.Vias
            : SelectedViasByRefs(project, resolver.Resolve(constraint.Source));

        return selected.Where(v => ViaInScope(v, project.Board.Layers, constraint.Scope));
    }

    private static IEnumerable<Via> SelectedViasByRefs(CanonicalProject project, IReadOnlyList<EntityReference> refs)
    {
        var viaIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "VIA").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        var netIds = refs.Where(r => ConstraintSelectorResolver.Canon(r.EntityType) == "NET").Select(r => r.EntityId).ToHashSet(StringComparer.Ordinal);
        if (viaIds.Count == 0 && netIds.Count == 0)
        {
            return [];
        }

        return project.PhysicalDesignState.Vias
            .Where(v => viaIds.Contains(v.Id.Value) || netIds.Contains(v.NetId.Value));
    }

    private static bool ViaInScope(Via via, IReadOnlyList<BoardLayer> layers, ConstraintScope scope)
    {
        var requestedLayers = scope.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        return requestedLayers.Count == 0 || ViaLayerSpan(via, layers).Any(l => requestedLayers.Contains(l.Value));
    }

    private static IReadOnlySet<LayerId> ViaLayerSpan(Via via, IReadOnlyList<BoardLayer> layers)
    {
        var start = layers.FirstOrDefault(l => l.Id == via.StartLayerId);
        var end = layers.FirstOrDefault(l => l.Id == via.EndLayerId);
        if (start is null || end is null)
        {
            return new HashSet<LayerId> { via.StartLayerId, via.EndLayerId };
        }

        var minOrder = Math.Min(start.Order, end.Order);
        var maxOrder = Math.Max(start.Order, end.Order);
        return layers
            .Where(l => l.IsCopperCapable && l.Order >= minOrder && l.Order <= maxOrder)
            .Select(l => l.Id)
            .ToHashSet();
    }

    private static IEnumerable<PhysicalObject> PhysicalObjects(PhysicalGeometryModel geometry, IReadOnlyList<EntityReference> refs, ConstraintSelector selector, ConstraintScope scope)
    {
        var all = geometry.Components.SelectMany(c => c.Objects)
            .Concat(geometry.RouteObjects)
            .Concat(geometry.ViaObjects)
            .Concat(geometry.CopperZoneObjects)
            .ToArray();
        if (ConstraintSelectorResolver.Canon(selector.Kind) == "ALL")
        {
            return FilterScope(all, scope);
        }

        return FilterScope(all.Where(o => refs.Any(r => MatchesPhysicalReference(geometry, o, r))), scope)
            .DistinctBy(o => o.Id);
    }

    private static IEnumerable<PhysicalObject> CopperObjects(PhysicalGeometryModel geometry, IReadOnlyList<EntityReference> refs, ConstraintSelector selector, ConstraintScope scope) =>
        PhysicalObjects(geometry, refs, selector, scope)
            .Where(o => o.Kind is PhysicalObjectKind.Pad or PhysicalObjectKind.Track or PhysicalObjectKind.Via or PhysicalObjectKind.CopperZone)
            .Where(o => o.NetId is not null);

    private static IEnumerable<PhysicalObject> FilterScope(IEnumerable<PhysicalObject> objects, ConstraintScope scope)
    {
        var layers = scope.LayerIds.Select(l => l.Value).ToHashSet(StringComparer.Ordinal);
        var kinds = scope.GeometryTypes.Select(ConstraintSelectorResolver.Canon).ToHashSet(StringComparer.Ordinal);
        return objects
            .Where(o => layers.Count == 0 || ObjectLayerIds(o).Any(layers.Contains))
            .Where(o => kinds.Count == 0 || kinds.Contains(ConstraintSelectorResolver.Canon(o.Kind.ToString())));
    }

    private static bool SameLayer(PhysicalObject first, PhysicalObject second)
    {
        var firstLayers = ObjectLayerIds(first).ToHashSet(StringComparer.Ordinal);
        var secondLayers = ObjectLayerIds(second).ToHashSet(StringComparer.Ordinal);
        return firstLayers.Count == 0 || secondLayers.Count == 0 || firstLayers.Overlaps(secondLayers);
    }

    private static IEnumerable<string> ObjectLayerIds(PhysicalObject obj)
    {
        if (obj.LayerId is not null)
        {
            yield return obj.LayerId.Value.Value;
        }

        if (obj.LayerSpan is not null)
        {
            foreach (var layer in obj.LayerSpan)
            {
                yield return layer.Value;
            }
        }
    }

    private static string PairKey(PhysicalObject first, PhysicalObject second)
    {
        var keys = new[] { $"{first.EntityType}:{first.EntityId}:{first.Id}", $"{second.EntityType}:{second.EntityId}:{second.Id}" }
            .Order(StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static bool MatchesPhysicalReference(PhysicalGeometryModel geometry, PhysicalObject obj, EntityReference reference)
    {
        var type = ConstraintSelectorResolver.Canon(reference.EntityType);
        return type switch
        {
            "NET" => obj.NetId is not null && string.Equals(obj.NetId.Value.Value, reference.EntityId, StringComparison.Ordinal),
            "ROUTE" => obj.RouteIds is not null && obj.RouteIds.Any(r => string.Equals(r.Value, reference.EntityId, StringComparison.Ordinal)),
            "TRACKSEGMENT" => obj.Kind == PhysicalObjectKind.Track && string.Equals(obj.EntityId, reference.EntityId, StringComparison.Ordinal),
            "VIA" => obj.Kind == PhysicalObjectKind.Via && string.Equals(obj.EntityId, reference.EntityId, StringComparison.Ordinal),
            "COPPERZONE" => obj.Kind == PhysicalObjectKind.CopperZone && string.Equals(obj.EntityId, reference.EntityId, StringComparison.Ordinal),
            "PAD" => obj.Kind == PhysicalObjectKind.Pad && string.Equals(obj.EntityId, reference.EntityId, StringComparison.Ordinal),
            "COMPONENT" => MatchesComponentReference(geometry, obj, reference.EntityId),
            _ => string.Equals(ConstraintSelectorResolver.Canon(obj.EntityType), type, StringComparison.Ordinal) &&
                string.Equals(obj.EntityId, reference.EntityId, StringComparison.Ordinal)
        };
    }

    private static bool MatchesComponentReference(PhysicalGeometryModel geometry, PhysicalObject obj, string componentId)
    {
        if (string.Equals(ConstraintSelectorResolver.Canon(obj.EntityType), "COMPONENT", StringComparison.Ordinal) &&
            string.Equals(obj.EntityId, componentId, StringComparison.Ordinal))
        {
            return true;
        }

        return geometry.Components.Any(c =>
            string.Equals(c.Component.Id.Value, componentId, StringComparison.Ordinal) &&
            c.Objects.Any(o => string.Equals(o.Id, obj.Id, StringComparison.Ordinal)));
    }

    private static bool AppliesTo(PhysicalObject keepout, PhysicalObjectKind kind)
    {
        if (keepout.AppliesTo is null || keepout.AppliesTo.Count == 0 || keepout.AppliesTo.Contains("ALL"))
        {
            return true;
        }

        var category = kind switch
        {
            PhysicalObjectKind.ComponentBody or PhysicalObjectKind.ComponentCourtyard or PhysicalObjectKind.Pad => "COMPONENTS",
            PhysicalObjectKind.Track => "TRACKS",
            PhysicalObjectKind.Via => "VIAS",
            PhysicalObjectKind.CopperZone => "COPPER_ZONES",
            _ => kind.ToString().ToUpperInvariant()
        };

        return keepout.AppliesTo.Contains(category);
    }

    private ConstraintEvaluation UnknownSelector(EffectiveConstraint constraint) =>
        Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference("CONSTRAINT", constraint.Id.Value)], null, null, ConstraintEvidence.From(("missing", "selector target")), "Constraint selector resolved no target.");

    private ConstraintEvaluation EmptySelectionResult(EffectiveConstraint constraint) =>
        ConstraintSelectorResolver.Canon(constraint.Source.Kind) == "ALL"
            ? Result(constraint, ConstraintEvaluationStatus.NotApplicable, [], null, null, ConstraintEvidence.Empty, null)
            : UnknownSelector(constraint);

    private ConstraintEvaluation UnknownGeometry(EffectiveConstraint constraint, string entityType, string entityId, string message) =>
        Result(constraint, ConstraintEvaluationStatus.Unknown, [new EntityReference(entityType, entityId)], null, null, ConstraintEvidence.From(("missing", "geometry")), message);

    private ConstraintEvaluation Result(EffectiveConstraint constraint, ConstraintEvaluationStatus status, IReadOnlyList<EntityReference> refs, LengthUnits? required, LengthUnits? actual, ConstraintEvidence evidence, string? message) =>
        new(constraint.Id, constraint.Type, constraint.Enforcement, status, refs, required, actual, evidence, constraint.Provenance, message);

    private static ReadinessReport WithRequiredUnknowns(ReadinessReport readiness, IReadOnlyList<ConstraintEvaluation> evaluations)
    {
        var issues = readiness.Issues.ToList();
        foreach (var evaluation in evaluations.Where(e => e.BlocksCandidate && e.Status == ConstraintEvaluationStatus.Unknown))
        {
            issues.Add(new ReadinessIssue(
                $"constraint[{evaluation.ConstraintId.Value}]",
                "ConstraintEvaluation",
                evaluation.Message ?? "Required constraint could not be evaluated because material data is unavailable.",
                Blocking: true,
                FallbackAvailable: false,
                evaluation.AffectedEntities));
        }

        var status = issues.Any(i => i.Blocking)
            ? ReadinessStatus.Blocked
            : issues.Count == 0 ? ReadinessStatus.Ready : ReadinessStatus.ReadyWithWarnings;

        return new ReadinessReport(status, issues, readiness.GeometryObjectsIndexed);
    }

    private static IEnumerable<PhysicalFinding> Findings(IEnumerable<ConstraintEvaluation> evaluations, IEnumerable<ConstraintConflict> conflicts)
    {
        foreach (var conflict in conflicts)
        {
            yield return new PhysicalFinding($"finding:conflict:{conflict.FirstConstraintId.Value}:{conflict.SecondConstraintId.Value}", "ERROR", "ConstraintConflict", conflict.Message, [new EntityReference("CONSTRAINT", conflict.FirstConstraintId.Value), new EntityReference("CONSTRAINT", conflict.SecondConstraintId.Value)], conflict.Evidence, Plan02DiagnosticCodes.ConstraintConflict, "OPEN");
        }

        foreach (var evaluation in evaluations.Where(e => e.Status is ConstraintEvaluationStatus.Fail or ConstraintEvaluationStatus.Unknown))
        {
            var code = evaluation.Status == ConstraintEvaluationStatus.Fail ? Plan02DiagnosticCodes.ConstraintFailed : Plan02DiagnosticCodes.ConstraintUnknown;
            yield return new PhysicalFinding(FindingId(evaluation), evaluation.BlocksCandidate ? "ERROR" : "WARNING", "ConstraintEvaluation", evaluation.Message ?? $"{evaluation.ConstraintType} evaluated as {evaluation.Status}.", evaluation.AffectedEntities, evaluation.Evidence, code, "OPEN");
        }
    }

    private static string FindingId(ConstraintEvaluation evaluation)
    {
        var entityFingerprint = evaluation.AffectedEntities.Count == 0
            ? "none"
            : string.Join("+", evaluation.AffectedEntities
                .Select(e => $"{ConstraintSelectorResolver.Canon(e.EntityType)}:{e.EntityId}")
                .Order(StringComparer.Ordinal));
        return $"finding:{evaluation.ConstraintId.Value}:{evaluation.Status}:{entityFingerprint}";
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

    private static LengthUnits? LengthParam(JsonElement parameters, LengthUnits? fallback, params string[] names)
    {
        var value = LongParam(parameters, names);
        return value is null ? fallback : new LengthUnits(value.Value);
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

    private static long DistanceToBoardEdges(GeometryPolygon container, GeometryPolygon candidate)
    {
        var min = double.PositiveInfinity;
        var boardRings = new[] { container.Outer }.Concat(container.Holes).ToArray();
        var containerEdges = boardRings.SelectMany(RingEdges).ToArray();
        var candidateEdges = RingEdges(candidate.Outer).ToArray();
        foreach (var point in candidate.Outer)
        {
            foreach (var edge in containerEdges)
            {
                min = Math.Min(min, PointSegmentDistanceSquared(point, edge));
            }
        }

        foreach (var point in boardRings.SelectMany(r => r))
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
