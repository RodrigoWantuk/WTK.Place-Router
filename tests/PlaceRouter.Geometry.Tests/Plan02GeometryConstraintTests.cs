using System.Text.Json;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.Domain.Model;
using PlaceRouter.Geometry;

namespace PlaceRouter.Geometry.Tests;

public sealed class Plan02GeometryConstraintTests
{
    private readonly IGeometryKernel _kernel = new ClipperGeometryKernel();

    [Fact]
    public void Component_pad_transform_handles_top_bottom_and_right_angle_rotation()
    {
        var project = Project(
            poses:
            [
                Pose("cmp_u1", 1000, 1000, 90, "TOP"),
                Pose("cmp_u2", 1000, 1000, 90, "BOTTOM")
            ]);

        var model = new PhysicalGeometryBuilder(_kernel).Build(project);

        var topPad = model.Component(new ComponentId("cmp_u1"))!.Pads.Single().Objects.Single(o => o.LayerId!.Value.Value == "layer_top_cu").Geometry.Envelope;
        var bottomPad = model.Component(new ComponentId("cmp_u2"))!.Pads.Single().Objects.Single(o => o.LayerId!.Value.Value == "layer_bottom_cu").Geometry.Envelope;

        Assert.Equal(new GeometryEnvelope(995, 1095, 1005, 1105), topPad);
        Assert.Equal(new GeometryEnvelope(995, 895, 1005, 905), bottomPad);
    }

    [Fact]
    public void Courtyard_overlap_required_fails_and_preferred_does_not_block_candidate()
    {
        var required = Project(
            poses: [Pose("cmp_u1", 1000, 1000), Pose("cmp_u2", 1050, 1000)],
            constraints: [Constraint("c_overlap", "ComponentOverlap", "REQUIRED", AllComponents(), EmptyParams())]);
        var preferred = Project(
            poses: [Pose("cmp_u1", 1000, 1000), Pose("cmp_u2", 1050, 1000)],
            constraints: [Constraint("c_overlap", "ComponentOverlap", "PREFERRED", AllComponents(), EmptyParams())]);

        var service = new ConstraintEvaluationService(_kernel);
        var requiredReport = service.Evaluate(required);
        var preferredReport = service.Evaluate(preferred);

        Assert.False(requiredReport.CandidateValid);
        Assert.Contains(requiredReport.Evaluations, e => e.ConstraintId.Value == "c_overlap" && e.Status == ConstraintEvaluationStatus.Fail && e.BlocksCandidate);
        Assert.True(preferredReport.CandidateValid);
        Assert.Contains(preferredReport.Evaluations, e => e.ConstraintId.Value == "c_overlap" && e.Status == ConstraintEvaluationStatus.Fail && !e.BlocksCandidate);
    }

    [Fact]
    public void Polygon_distance_and_clearance_boundary_are_deterministic()
    {
        var a = Rect(0, 0, 100, 100);
        var touching = Rect(100, 0, 200, 100);
        var separated = Rect(250, 0, 350, 100);

        Assert.Equal(0, _kernel.Distance(a, touching).Value);
        Assert.Equal(150, _kernel.Distance(a, separated).Value);
        Assert.False(_kernel.Intersects(a, touching));
    }

    [Fact]
    public void Spatial_query_uses_broad_phase_and_exact_phase_filters_false_positive()
    {
        var triangle = new PhysicalObject("tri", "COMPONENT", "cmp_tri", PhysicalObjectKind.ComponentCourtyard, new GeometryPolygon([new GeometryPoint(0, 0), new GeometryPoint(1000, 0), new GeometryPoint(0, 1000)], []), null, null);
        var square = new PhysicalObject("square", "COMPONENT", "cmp_square", PhysicalObjectKind.ComponentCourtyard, Rect(2000, 2000, 2200, 2200), null, null);
        var index = PhysicalObjectIndex.Build(_kernel, [triangle, square]);
        var query = Rect(900, 900, 950, 950);

        var result = index.QueryExact(query);

        Assert.Equal(2, index.Count);
        Assert.Equal(1, result.BroadPhaseCandidates);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Manufacturing_minimum_track_width_is_applied_as_effective_required_rule()
    {
        var project = Project(routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 221, 1000, 1000, 2000, 1000))], manufacturingMinimumTraceWidth: 222);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.False(report.CandidateValid);
        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "mfg_min_track_width" && e.Status == ConstraintEvaluationStatus.Fail && e.RequiredUnits!.Value.Value == 222);
        Assert.Contains(report.Findings, f => f.Source == Plan02DiagnosticCodes.ConstraintFailed && f.Evidence.Values.ContainsKey("trackWidthUnits"));
    }

    [Fact]
    public void Explicit_more_specific_minimum_track_width_refines_manufacturing_rule()
    {
        var project = Project(
            routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 250, 1000, 1000, 2000, 1000))],
            manufacturingMinimumTraceWidth: 150,
            constraints:
            [
                Constraint("c_net_width", "MinimumTrackWidth", "REQUIRED", Entity("NET", "net_in"), Param(("minimumUnits", 300)))
            ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        var effective = new EffectiveConstraintResolver()
            .Resolve(project, ManufacturingRuleResolver.Resolve(project.ManufacturingProfile))
            .EffectiveFor(new EntityReference("NET", "net_in"), "MinimumTrackWidth", new ConstraintSelectorResolver(project));

        Assert.Equal("c_net_width", effective!.Id.Value);
        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "c_net_width" && e.Status == ConstraintEvaluationStatus.Fail && e.RequiredUnits!.Value.Value == 300);
    }

    [Fact]
    public void Required_allowed_rotation_contradiction_produces_conflict()
    {
        var project = Project(constraints:
        [
            Constraint("rot_0", "AllowedRotation", "REQUIRED", Entity("COMPONENT", "cmp_u1"), ParamArray("allowedDegrees", 0)),
            Constraint("rot_90", "AllowedRotation", "REQUIRED", Entity("COMPONENT", "cmp_u1"), ParamArray("allowedDegrees", 90))
        ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.False(report.CandidateValid);
        Assert.Contains(report.Conflicts, c => c.FirstConstraintId.Value == "rot_0" && c.SecondConstraintId.Value == "rot_90");
        Assert.Contains(report.Findings, f => f.Source == Plan02DiagnosticCodes.ConstraintConflict);
    }

    [Fact]
    public void Missing_footprint_geometry_is_blocking_readiness_unknown()
    {
        var project = Project(components:
        [
            Component("cmp_u1", "U1", null),
            Component("cmp_u2", "U2", "fp_demo")
        ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Equal(ReadinessStatus.Blocked, report.Readiness.Status);
        Assert.Contains(report.Readiness.Issues, i => i.Field == "component[cmp_u1].footprintId" && i.Blocking && !i.FallbackAvailable);
    }

    [Fact]
    public void Plan02_demo_report_summarizes_index_constraints_preferences_and_readiness()
    {
        var project = Project(
            poses: [Pose("cmp_u1", 1000, 1000), Pose("cmp_u2", 3000, 1000)],
            constraints:
            [
                Constraint("bounds", "BoardBounds", "REQUIRED", AllComponents(), EmptyParams()),
                Constraint("pref_sep", "MinimumSeparation", "PREFERRED", AllComponents(), Param(("distanceUnits", 3000)))
            ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);
        var summary = report.SummaryLine();

        Assert.Contains("Geometry objects indexed:", summary);
        Assert.Contains("Required constraints:", summary);
        Assert.Contains("Preferences: 1 violations", summary);
        Assert.Equal(ReadinessStatus.Ready, report.Readiness.Status);
    }

    [Fact]
    public void Missing_material_required_parameter_produces_unknown_and_blocks_candidate()
    {
        var project = Project(
            routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 250, 1000, 1000, 2000, 1000))],
            omitMinimumTraceWidth: true);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.False(report.CandidateValid);
        Assert.Equal(ReadinessStatus.Blocked, report.Readiness.Status);
        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "mfg_min_track_width" && e.Status == ConstraintEvaluationStatus.Unknown);
    }

    [Fact]
    public void Required_unknown_constraint_parameter_blocks_candidate()
    {
        var project = Project(constraints: [Constraint("max_vias", "MaximumVias", "REQUIRED", Entity("NET", "net_in"), EmptyParams())]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.False(report.CandidateValid);
        Assert.Contains(report.Readiness.Issues, i => i.Field == "constraint[max_vias]" && i.Blocking);
    }

    [Fact]
    public void Pads_are_bound_to_nets_and_participate_in_clearance()
    {
        var project = Project(
            nets:
            [
                Net("net_in", "IN", [new NetEndpoint(new ComponentId("cmp_u1"), new PadId("pad_1"), null)]),
                Net("net_gnd", "GND", [new NetEndpoint(new ComponentId("cmp_u2"), new PadId("pad_1"), null)])
            ],
            poses: [Pose("cmp_u1", 1000, 1000), Pose("cmp_u2", 1015, 1000)]);

        var model = new PhysicalGeometryBuilder(_kernel).Build(project);
        var padNet = model.Component(new ComponentId("cmp_u1"))!.Pads.Single().Objects.Single().NetId;
        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Equal(new NetId("net_in"), padNet);
        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "mfg_min_clearance" && e.Status == ConstraintEvaluationStatus.Fail && e.AffectedEntities.Any(a => a.EntityType == "PAD"));
    }

    [Fact]
    public void Diagonal_track_exact_geometry_does_not_fill_its_aabb_corner()
    {
        var track = Track("trk_diag", "layer_top_cu", 100, 0, 0, 1000, 1000);
        var route = Route("route_diag", "net_in", track);
        var model = new PhysicalGeometryBuilder(_kernel).Build(Project(routes: [route]));
        var diagonal = model.RouteObjects.Single(o => o.Id == "trk_diag");

        Assert.False(_kernel.Contains(diagonal.Geometry, new GeometryPoint(900, 100)));
    }

    [Fact]
    public void Via_exact_geometry_is_polygonal_circle_not_bounding_square()
    {
        var via = new Via(new ViaId("via_1"), new NetId("net_in"), new Point2(new LengthUnits(1000), new LengthUnits(1000)), new LayerId("layer_top_cu"), new LayerId("layer_bottom_cu"), "THROUGH", new LengthUnits(300), new LengthUnits(600), "REROUTABLE", Json("{}"));
        var model = new PhysicalGeometryBuilder(_kernel).Build(Project(vias: [via]));
        var viaObject = model.ViaObjects.Single();

        Assert.False(_kernel.Contains(viaObject.Geometry, new GeometryPoint(1300, 1300)));
    }

    [Fact]
    public void Keepout_respects_applies_to_and_layer()
    {
        var keepout = new Keepout(new KeepoutId("ko_tracks"), "Track KO", Rect(900, 900, 2100, 1100).ToPolygon2(), [new LayerId("layer_top_cu")], ["TRACKS"], null);
        var project = Project(
            keepouts: [keepout],
            routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 250, 1000, 1000, 2000, 1000))],
            constraints: [Constraint("ko", "Keepout", "REQUIRED", AllObjects(), EmptyParams())]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "ko" && e.Status == ConstraintEvaluationStatus.Fail && e.AffectedEntities.Any(a => a.EntityType == "TRACK_SEGMENT"));
        Assert.DoesNotContain(report.Evaluations, e => e.ConstraintId.Value == "ko" && e.Status == ConstraintEvaluationStatus.Fail && e.AffectedEntities.Any(a => a.EntityType == "COMPONENT"));
    }

    [Fact]
    public void Board_cutout_hole_prevents_containment()
    {
        var board = new GeometryPolygon(
            Rect(0, 0, 1000, 1000).Outer,
            [Rect(400, 400, 600, 600).Outer]);

        Assert.False(_kernel.Contains(board, Rect(350, 350, 650, 650)));
    }

    [Fact]
    public void Offset_preserves_cutout_hole_topology()
    {
        var board = new GeometryPolygon(
            Rect(0, 0, 1000, 1000).Outer,
            [Rect(400, 400, 600, 600).Outer]);

        var offset = _kernel.Offset(board, new LengthUnits(0)).Single();

        Assert.NotEmpty(offset.Holes);
        Assert.False(_kernel.Contains(offset, new GeometryPoint(500, 500)));
    }

    [Fact]
    public void Selector_without_targets_does_not_expand_to_all_components()
    {
        var project = Project(constraints: [Constraint("bad_selector", "BoardBounds", "REQUIRED", Entity("NET", "net_in"), EmptyParams())]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "bad_selector" && e.Status == ConstraintEvaluationStatus.Unknown);
    }

    [Fact]
    public void Finding_ids_include_affected_entities()
    {
        var project = Project(routes:
        [
            Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 100, 1000, 1000, 2000, 1000)),
            Route("route_2", "net_gnd", Track("trk_2", "layer_top_cu", 100, 1000, 1200, 2000, 1200))
        ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);
        var widthFindings = report.Findings.Where(f => f.Source == Plan02DiagnosticCodes.ConstraintFailed && f.Id.Contains("mfg_min_track_width", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, widthFindings.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Effective_minimum_track_width_uses_strictest_required_rule_for_evaluation()
    {
        var project = Project(
            routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 150, 1000, 1000, 2000, 1000))],
            manufacturingMinimumTraceWidth: 300,
            constraints: [Constraint("c_net_width", "MinimumTrackWidth", "REQUIRED", Entity("NET", "net_in"), Param(("minimumUnits", 100)))]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);
        var effective = new EffectiveConstraintResolver()
            .Resolve(project, ManufacturingRuleResolver.Resolve(project.ManufacturingProfile))
            .EffectiveFor(new EntityReference("NET", "net_in"), null, new ConstraintScope([new LayerId("layer_top_cu")], null, null, ["TRACK"]), "MinimumTrackWidth", new ConstraintSelectorResolver(project));

        Assert.Equal("mfg_min_track_width", effective!.Id.Value);
        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "mfg_min_track_width" && e.Status == ConstraintEvaluationStatus.Fail && e.RequiredUnits!.Value.Value == 300);
        Assert.DoesNotContain(report.Evaluations, e => e.ConstraintId.Value == "c_net_width");
    }

    [Fact]
    public void Effective_set_reports_conflict_for_overlapping_allowed_sets()
    {
        var project = Project(constraints:
        [
            Constraint("all_top", "AllowedSide", "REQUIRED", AllComponents(), Json("""{"allowedSides":["TOP"]}""")),
            Constraint("u1_bottom", "AllowedSide", "REQUIRED", Entity("COMPONENT", "cmp_u1"), Json("""{"allowedSides":["BOTTOM"]}"""))
        ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Contains(report.Conflicts, c => c.FirstConstraintId.Value == "all_top" && c.SecondConstraintId.Value == "u1_bottom");
    }

    [Fact]
    public void Route_selector_that_is_not_route_or_net_does_not_expand_to_all_routes()
    {
        var project = Project(
            routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 250, 1000, 1000, 2000, 1000))],
            constraints: [Constraint("bad_length_selector", "MaximumLength", "REQUIRED", Entity("COMPONENT", "cmp_u1"), Param(("maximumUnits", 10)))]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "bad_length_selector" && e.Status == ConstraintEvaluationStatus.Unknown);
        Assert.DoesNotContain(report.Evaluations, e => e.ConstraintId.Value == "bad_length_selector" && e.AffectedEntities.Any(a => a.EntityType == "ROUTE"));
    }

    [Fact]
    public void Explicit_via_constraint_respects_net_selector_and_layer_scope()
    {
        var vias = new[]
        {
            Via("via_in", "net_in", "MICROVIA", "layer_top_cu", "layer_bottom_cu"),
            Via("via_gnd", "net_gnd", "MICROVIA", "layer_top_cu", "layer_bottom_cu")
        };
        var project = Project(
            vias: vias,
            constraints:
            [
                Constraint(
                    "via_type_net_in",
                    "AllowedViaTypes",
                    "REQUIRED",
                    Entity("NET", "net_in"),
                    Json("""{"allowedViaTypes":["THROUGH"]}"""),
                    scope: new ConstraintScope([new LayerId("layer_top_cu")], null, null, []))
            ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.Contains(report.Evaluations, e => e.ConstraintId.Value == "via_type_net_in" && e.Status == ConstraintEvaluationStatus.Fail && e.AffectedEntities.Any(a => a.EntityId == "via_in"));
        Assert.DoesNotContain(report.Evaluations, e => e.ConstraintId.Value == "via_type_net_in" && e.AffectedEntities.Any(a => a.EntityId == "via_gnd"));
    }

    [Fact]
    public void Vertical_oval_pad_uses_vertical_capsule_not_circle()
    {
        var ovalPad = new Pad(new PadId("pad_1"), "1", null, null, new Point2(new LengthUnits(0), new LengthUnits(0)), AngleDegrees.Zero, "OVAL", new LengthUnits(1000), new LengthUnits(2000), null, "SMD", [new LayerId("layer_top_cu")], null, null, null);
        var project = Project(footprints: [Footprint(ovalPad)], poses: [Pose("cmp_u1", 2000, 1500)]);

        var pad = new PhysicalGeometryBuilder(_kernel).Build(project).Component(new ComponentId("cmp_u1"))!.Pads.Single().Objects.Single();

        Assert.Equal(new GeometryEnvelope(1500, 500, 2500, 2500), pad.Geometry.Envelope);
        Assert.True(_kernel.Contains(pad.Geometry, new GeometryPoint(2000, 2499)));
        Assert.False(_kernel.Contains(pad.Geometry, new GeometryPoint(2999, 1500)));
    }

    [Fact]
    public void Track_capsule_contains_round_caps_at_line_endpoints()
    {
        var model = new PhysicalGeometryBuilder(_kernel).Build(Project(routes: [Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 100, 0, 0, 1000, 0))]));
        var track = model.RouteObjects.Single(o => o.Id == "trk_1");

        Assert.True(_kernel.Contains(track.Geometry, new GeometryPoint(-49, 0)));
        Assert.True(_kernel.Contains(track.Geometry, new GeometryPoint(1049, 0)));
        Assert.False(_kernel.Contains(track.Geometry, new GeometryPoint(-51, 0)));
        Assert.False(_kernel.Contains(track.Geometry, new GeometryPoint(1051, 0)));
    }

    [Fact]
    public void Blind_via_layer_span_does_not_collide_with_unspanned_layer_scope()
    {
        var layers = FourCopperLayers();
        var via = Via("via_l1_l2", "net_in", "BLIND", "layer_l1_cu", "layer_l2_cu", 1000, 1000);
        var track = Track("trk_l3", "layer_l3_cu", 250, 1000, 1000, 2000, 1000);
        var project = Project(
            boardLayers: layers,
            vias: [via],
            routes: [Route("route_l3", "net_gnd", track)],
            constraints:
            [
                Constraint(
                    "l3_clearance",
                    "MinimumClearance",
                    "REQUIRED",
                    AllObjects(),
                    Param(("minimumUnits", 500)),
                    scope: new ConstraintScope([new LayerId("layer_l3_cu")], null, null, []))
            ]);

        var model = new PhysicalGeometryBuilder(_kernel).Build(project);
        var viaObject = model.ViaObjects.Single();
        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);

        Assert.DoesNotContain(new LayerId("layer_l3_cu"), viaObject.LayerSpan!);
        Assert.DoesNotContain(report.Evaluations, e => e.ConstraintId.Value == "l3_clearance" && e.Status == ConstraintEvaluationStatus.Fail && e.AffectedEntities.Any(a => a.EntityId == "via_l1_l2"));
    }

    [Fact]
    public void Minimum_clearance_does_not_emit_duplicate_reverse_pairs()
    {
        var project = Project(routes:
        [
            Route("route_1", "net_in", Track("trk_1", "layer_top_cu", 250, 1000, 1000, 2000, 1000)),
            Route("route_2", "net_gnd", Track("trk_2", "layer_top_cu", 250, 1000, 1200, 2000, 1200))
        ]);

        var report = new ConstraintEvaluationService(_kernel).Evaluate(project);
        var clearanceFailures = report.Evaluations.Where(e => e.ConstraintId.Value == "mfg_min_clearance" && e.Status == ConstraintEvaluationStatus.Fail).ToArray();

        Assert.Equal(clearanceFailures.Length, clearanceFailures.Select(e => e.AffectedEntities.Select(a => $"{a.EntityType}:{a.EntityId}").Order(StringComparer.Ordinal).Aggregate((a, b) => a + "|" + b)).Distinct().Count());
    }

    private static CanonicalProject Project(
        IReadOnlyList<Component>? components = null,
        IReadOnlyList<Footprint>? footprints = null,
        IReadOnlyList<Net>? nets = null,
        IReadOnlyList<ComponentPose>? poses = null,
        IReadOnlyList<ConstraintDefinition>? constraints = null,
        IReadOnlyList<Route>? routes = null,
        IReadOnlyList<Via>? vias = null,
        IReadOnlyList<Keepout>? keepouts = null,
        IReadOnlyList<BoardLayer>? boardLayers = null,
        long manufacturingMinimumTraceWidth = 150,
        bool omitMinimumTraceWidth = false) =>
        new(
            CanonicalProject.CurrentSchemaVersion,
            new ProjectId("project_plan02"),
            1,
            new ProjectMetadata("PLAN-02 fixture", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, []),
            [],
            new LogicalDesign(
                components ?? [Component("cmp_u1", "U1", "fp_demo"), Component("cmp_u2", "U2", "fp_demo")],
                footprints ?? [Footprint()],
                nets ?? [Net("net_in", "IN"), Net("net_gnd", "GND")],
                [],
                []),
            Board(keepouts, boardLayers),
            Manufacturing(manufacturingMinimumTraceWidth, omitMinimumTraceWidth),
            constraints ?? [],
            new Semantics([]),
            new PhysicalDesignState(new PhysicalStateId("state_1"), 1, "ACCEPTED", 1, poses ?? [Pose("cmp_u1", 1000, 1000), Pose("cmp_u2", 3000, 1000)], routes ?? [], vias ?? [], [], null, null),
            [],
            new ProjectSettings(Json("{}"), [], null),
            Json("{}"));

    private static Component Component(string id, string refdes, string? footprintId) =>
        new(new ComponentId(id), refdes, null, null, null, footprintId is null ? null : new FootprintId(footprintId), "MOVABLE", new Dictionary<string, SourcedValue>(), null, Json("{}"), Provenance.UserDefined);

    private static Footprint Footprint(Pad? pad = null) =>
        new(
            new FootprintId("fp_demo"),
            "Demo footprint",
            new Point2(new LengthUnits(0), new LengthUnits(0)),
            Rect(-100, -100, 100, 100).ToPolygon2(),
            Rect(-150, -150, 150, 150).ToPolygon2(),
            null,
            [pad ?? new Pad(new PadId("pad_1"), "1", null, null, new Point2(new LengthUnits(100), new LengthUnits(0)), AngleDegrees.Zero, "RECT", new LengthUnits(10), new LengthUnits(10), null, "SMD", [new LayerId("layer_top_cu")], null, null, null)],
            [],
            [],
            Provenance.UserDefined);

    private static BoardDefinition Board(IReadOnlyList<Keepout>? keepouts = null, IReadOnlyList<BoardLayer>? layers = null) =>
        new(
            new Point2(new LengthUnits(0), new LengthUnits(0)),
            Rect(0, 0, 5000, 3000).ToPolygon2(),
            [],
            [],
            null,
            null,
            layers ?? TwoCopperLayers(),
            [],
            [],
            keepouts ?? []);

    private static IReadOnlyList<BoardLayer> TwoCopperLayers() =>
        [
            new BoardLayer(new LayerId("layer_top_cu"), "Top copper", "COPPER_SIGNAL", 1, null, null, new Dictionary<string, SourcedValue>()),
            new BoardLayer(new LayerId("layer_bottom_cu"), "Bottom copper", "COPPER_SIGNAL", 2, null, null, new Dictionary<string, SourcedValue>())
        ];

    private static IReadOnlyList<BoardLayer> FourCopperLayers() =>
        [
            new BoardLayer(new LayerId("layer_l1_cu"), "L1 copper", "COPPER_SIGNAL", 1, null, null, new Dictionary<string, SourcedValue>()),
            new BoardLayer(new LayerId("layer_l2_cu"), "L2 copper", "COPPER_SIGNAL", 2, null, null, new Dictionary<string, SourcedValue>()),
            new BoardLayer(new LayerId("layer_l3_cu"), "L3 copper", "COPPER_SIGNAL", 3, null, null, new Dictionary<string, SourcedValue>()),
            new BoardLayer(new LayerId("layer_l4_cu"), "L4 copper", "COPPER_SIGNAL", 4, null, null, new Dictionary<string, SourcedValue>())
        ];

    private static ManufacturingProfile Manufacturing(long minimumTraceWidth, bool omitMinimumTraceWidth) =>
        new(
            new StableId("mfg_demo"),
            "Demo manufacturing",
            "1",
            null,
            null,
            ManufacturingCapabilities(minimumTraceWidth, omitMinimumTraceWidth),
            Provenance.UserDefined);

    private static Dictionary<string, SourcedValue> ManufacturingCapabilities(long minimumTraceWidth, bool omitMinimumTraceWidth)
    {
        var values = new Dictionary<string, SourcedValue>
        {
                ["minimumSpacing"] = Sourced(150),
                ["minimumDrill"] = Sourced(300),
                ["minimumViaDiameter"] = Sourced(600),
                ["minimumAnnularRing"] = Sourced(100),
                ["copperToEdge"] = Sourced(250),
                ["minimumComponentSpacing"] = Sourced(0),
                ["allowedViaTypes"] = SourcedArray("THROUGH"),
                ["allowedLayerCounts"] = SourcedArray("2")
        };
        if (!omitMinimumTraceWidth)
        {
            values["minimumTraceWidth"] = Sourced(minimumTraceWidth);
        }

        return values;
    }

    private static SourcedValue Sourced(long value) =>
        new(Json(value.ToString()), "um", "KNOWN", null, Provenance.UserDefined);

    private static SourcedValue SourcedArray(params string[] values) =>
        new(Json("[" + string.Join(",", values.Select(v => "\"" + v + "\"")) + "]"), null, "KNOWN", null, Provenance.UserDefined);

    private static Net Net(string id, string name, IReadOnlyList<NetEndpoint>? endpoints = null) =>
        new(new NetId(id), name, endpoints ?? [], null, new Dictionary<string, SourcedValue>(), new Dictionary<string, SourcedValue>(), Provenance.UserDefined);

    private static ComponentPose Pose(string componentId, long x, long y, decimal rotation = 0, string side = "TOP") =>
        new(new ComponentId(componentId), new Point2(new LengthUnits(x), new LengthUnits(y)), new AngleDegrees(rotation), side, "PLACED", null);

    private static Route Route(string id, string netId, params TrackSegment[] tracks) =>
        new(new RouteId(id), new NetId(netId), "ROUTED", "REROUTABLE", tracks, [], Provenance.UserDefined, Json("{}"));

    private static TrackSegment Track(string id, string layerId, long width, long x1, long y1, long x2, long y2) =>
        new(new TrackSegmentId(id), "LINE", new LayerId(layerId), new LengthUnits(width), new Point2(new LengthUnits(x1), new LengthUnits(y1)), new Point2(new LengthUnits(x2), new LengthUnits(y2)), null, null);

    private static Via Via(string id, string netId, string viaType, string startLayer, string endLayer, long x = 1000, long y = 1000) =>
        new(new ViaId(id), new NetId(netId), new Point2(new LengthUnits(x), new LengthUnits(y)), new LayerId(startLayer), new LayerId(endLayer), viaType, new LengthUnits(300), new LengthUnits(600), "REROUTABLE", Json("{}"));

    private static ConstraintDefinition Constraint(string id, string type, string enforcement, ConstraintSelector source, JsonElement parameters, ConstraintSelector? target = null, ConstraintScope? scope = null) =>
        new(new ConstraintId(id), type, source, target, parameters, enforcement, scope ?? new ConstraintScope([], null, null, []), Provenance.UserDefined, null, true);

    private static ConstraintSelector AllComponents() =>
        new("ALL", "COMPONENT", [], null);

    private static ConstraintSelector AllObjects() =>
        new("ALL", null, [], null);

    private static ConstraintSelector Entity(string type, string id) =>
        new("ENTITY", type, [id], null);

    private static JsonElement EmptyParams() => Json("{}");

    private static JsonElement Param(params (string Key, long Value)[] values) =>
        Json("{" + string.Join(",", values.Select(v => $"\"{v.Key}\":{v.Value}")) + "}");

    private static JsonElement ParamArray(string key, params long[] values) =>
        Json("{\"" + key + "\":[" + string.Join(",", values) + "]}");

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static GeometryPolygon Rect(long minX, long minY, long maxX, long maxY) =>
        new(
            [
                new GeometryPoint(minX, minY),
                new GeometryPoint(maxX, minY),
                new GeometryPoint(maxX, maxY),
                new GeometryPoint(minX, maxY)
            ],
            []);
}
