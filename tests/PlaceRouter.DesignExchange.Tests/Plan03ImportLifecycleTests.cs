using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PlaceRouter.Application.Lifecycle;
using PlaceRouter.Application.Projects;
using PlaceRouter.Cli;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.DesignExchange.Specctra;
using PlaceRouter.Domain.Model;

namespace PlaceRouter.DesignExchange.Tests;

public sealed class Plan03ImportLifecycleTests
{
    [Fact]
    public void Dsn_import_maps_board_components_pads_nets_and_fingerprint()
    {
        using var temp = new TempDirectory();
        var source = WriteDsn(temp.Path);
        var service = Service();

        var result = service.ImportDesign(new ImportRequest(source, SourceRetentionPolicy.ReferenceOnly));

        Assert.True(result.Success, Messages(result.Diagnostics));
        Assert.NotNull(result.SourceFingerprint);
        Assert.Equal(SourceHash(source), result.SourceFingerprint!.Sha256);
        Assert.Equal("COMPLETE", result.Capabilities["components"]);
        Assert.Equal("PARTIAL", result.Capabilities["rules"]);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ImportLoss && !d.Blocking);

        var project = result.Project!;
        Assert.Equal("DemoBoard", project.Metadata.Name);
        Assert.Equal(2, project.Board.Layers.Count);
        Assert.NotNull(project.Board.Outline);
        Assert.Equal(2, project.LogicalDesign.Components.Count);
        Assert.Single(project.LogicalDesign.Footprints);
        Assert.Equal(2, project.LogicalDesign.Footprints.Single().Pads.Count);
        Assert.Equal(2, project.LogicalDesign.Nets.Count);
        Assert.Equal("UNKNOWN", project.Board.Material!.Status);
        Assert.Equal("KNOWN", project.ManufacturingProfile.Capabilities["minimumTrackWidth"].Status);
    }

    [Fact]
    public void Dsn_import_reports_missing_physical_data_without_inventing_defaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "partial.dsn");
        File.WriteAllText(path, PartialDsn, Encoding.UTF8);

        var result = Service().ImportDesign(new ImportRequest(path, SourceRetentionPolicy.ReferenceOnly));

        Assert.True(result.Success, Messages(result.Diagnostics));
        Assert.Equal("MISSING", result.Capabilities["layers"]);
        Assert.Equal("MISSING", result.Capabilities["footprints"]);
        Assert.Equal("MISSING", result.Capabilities["componentPlacement"]);
        Assert.Empty(result.Project!.Board.Layers);
        Assert.Null(result.Project.LogicalDesign.Components.Single().FootprintId);
        Assert.Empty(result.Project.PhysicalDesignState.ComponentPoses);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ImportLoss && d.Message.Contains("no layers were invented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_save_reopen_preserves_project_and_embedded_source()
    {
        using var temp = new TempDirectory();
        var source = WriteDsn(temp.Path);
        var service = Service();
        var imported = service.ImportDesign(new ImportRequest(source, SourceRetentionPolicy.Embed));
        Assert.True(imported.Success, Messages(imported.Diagnostics));

        var prdx = Path.Combine(temp.Path, "demo.prdx");
        var save = service.SaveProject(imported.Document!, prdx);
        Assert.True(save.Success, Messages(save.Diagnostics));

        var reopened = service.LoadProject(prdx);
        Assert.True(reopened.Success, Messages(reopened.Diagnostics));
        Assert.Equal(imported.Project!.Summary, reopened.Project!.Summary);
        Assert.Equal(imported.Project.SourceImports.Single().SourceSha256, reopened.Project.SourceImports.Single().SourceSha256);
        Assert.Equal("source/" + Path.GetFileName(source), reopened.Project.SourceImports.Single().EmbeddedPath);

        using var archive = ZipFile.OpenRead(prdx);
        Assert.NotNull(archive.GetEntry("source/" + Path.GetFileName(source)));
    }

    [Fact]
    public void Move_transaction_diff_reports_affected_component_nets_and_route_scope()
    {
        var session = new ProjectSession(ImportedDocument());
        var transaction = session.BeginTransaction("move U1", "test");

        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2000), new(2200)), "test"));
        var diff = transaction.Diff;

        Assert.Contains(diff.AffectedComponents, r => r.EntityType == "COMPONENT" && r.EntityId == "cmp_u1");
        Assert.Contains(diff.AffectedNets, r => r.EntityId == "net_n1");
        Assert.Contains(diff.AffectedNets, r => r.EntityId == "net_gnd");

        var recovery = session.Apply(transaction);
        Assert.Equal(1, session.ProjectRevision);
        Assert.Equal(1, session.PhysicalStateRevision);
        Assert.Contains(DerivedArtifactStage.AbsoluteGeometry, recovery.Impact.InvalidStages);
    }

    [Fact]
    public void Impact_planner_distinguishes_metadata_physical_and_manufacturing_edits()
    {
        var session = new ProjectSession(ImportedDocument());
        var planner = new EditImpactPlanner();

        var metadata = session.BeginTransaction("rename", "test");
        metadata.Apply(new UpdateProjectMetadataAction("Renamed", "metadata only"));
        var metadataImpact = planner.Plan(metadata.Commit().Diff, DependencyGraph.From(session.Document.Project));
        Assert.Equal(DerivedArtifactStage.None, metadataImpact.EarliestInvalidStage);
        Assert.False(metadataImpact.RequiresPhysicalGeometryRebuild);

        var physical = session.BeginTransaction("move", "test");
        physical.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2100), new(2200)), "test"));
        var physicalImpact = planner.Plan(physical.Commit().Diff, DependencyGraph.From(session.Document.Project));
        Assert.Contains(DerivedArtifactStage.AbsoluteGeometry, physicalImpact.InvalidStages);
        Assert.True(physicalImpact.RequiresRouteRecovery);

        var manufacturing = session.BeginTransaction("rule", "test");
        manufacturing.Apply(new UpdateManufacturingCapabilityAction("minimumClearance", KnownUm(250)));
        var manufacturingImpact = planner.Plan(manufacturing.Commit().Diff, DependencyGraph.From(session.Document.Project));
        Assert.Contains(DerivedArtifactStage.ConstraintResolution, manufacturingImpact.InvalidStages);
        Assert.Contains(DerivedArtifactStage.DetailedRoutes, manufacturingImpact.InvalidStages);
        Assert.DoesNotContain(DerivedArtifactStage.AbsoluteGeometry, manufacturingImpact.InvalidStages);
    }

    [Fact]
    public void Rollback_restores_base_semantic_state()
    {
        var session = new ProjectSession(ImportedDocument());
        var basePosition = Pose(session.Document.Project, "cmp_u1").Position;
        var transaction = session.BeginTransaction("move then cancel", "test");

        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(9999), new(9999)), "test"));
        var rolledBack = transaction.Rollback();

        Assert.Equal(basePosition, Pose(rolledBack.Project, "cmp_u1").Position);
        Assert.Equal(0, session.ProjectRevision);
        Assert.Equal(0, session.PhysicalStateRevision);
    }

    [Fact]
    public void Recovery_journal_replays_committed_transaction_after_restart()
    {
        using var temp = new TempDirectory();
        var baseDocument = ImportedDocument();
        var journal = new FileRecoveryJournal(temp.Path);
        var session = new ProjectSession(baseDocument, journal);

        var transaction = session.BeginTransaction("move U2", "test");
        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u2"), new Point2(new(4200), new(1200)), "test"));
        session.Apply(transaction);

        var recovered = journal.Replay(baseDocument);

        Assert.Equal(new Point2(new(4200), new(1200)), Pose(recovered.Project, "cmp_u2").Position);
        Assert.Equal(1, recovered.Project.ProjectRevision);
        Assert.Equal(1, recovered.Project.PhysicalDesignState.StateRevision);
    }

    [Fact]
    public void Undo_and_redo_use_session_transactions()
    {
        var session = new ProjectSession(ImportedDocument());
        var basePosition = Pose(session.Document.Project, "cmp_u1").Position;
        var transaction = session.BeginTransaction("move U1", "test");
        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2600), new(2700)), "test"));
        session.Apply(transaction);

        session.Undo();
        Assert.Equal(basePosition, Pose(session.Document.Project, "cmp_u1").Position);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Equal(new Point2(new(2600), new(2700)), Pose(session.Document.Project, "cmp_u1").Position);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void Undo_and_new_edit_keep_revisions_monotonic_and_run_baseline_stale()
    {
        var session = new ProjectSession(ImportedDocument());
        var runs = new RunBaselineService();
        var first = session.BeginTransaction("move U1", "test");
        first.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2600), new(2700)), "test"));
        session.Apply(first);
        var run = runs.StartRun(session);

        session.Undo();
        var second = session.BeginTransaction("move U2", "test");
        second.Apply(new MoveComponentAction(new ComponentId("cmp_u2"), new Point2(new(3600), new(3700)), "test"));
        session.Apply(second);

        Assert.Equal(3, session.ProjectRevision);
        Assert.Equal(3, session.PhysicalStateRevision);
        Assert.False(runs.CanCommit(run, session).CanCommit);
    }

    [Fact]
    public void Recovery_journal_preserves_undo_sequence_after_restart()
    {
        using var temp = new TempDirectory();
        var baseDocument = ImportedDocument();
        var journal = new FileRecoveryJournal(temp.Path);
        var session = new ProjectSession(baseDocument, journal);
        var basePosition = Pose(baseDocument.Project, "cmp_u1").Position;
        var transaction = session.BeginTransaction("move U1", "test");
        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2600), new(2700)), "test"));
        session.Apply(transaction);

        session.Undo();

        var recovered = journal.Replay(baseDocument);

        Assert.Equal(basePosition, Pose(recovered.Project, "cmp_u1").Position);
        Assert.Equal(2, recovered.Project.ProjectRevision);
        Assert.Equal(2, recovered.Project.PhysicalDesignState.StateRevision);
    }

    [Fact]
    public void Recovery_journal_replays_metadata_manufacturing_constraint_group_and_region_actions()
    {
        using var temp = new TempDirectory();
        var baseDocument = ImportedDocument();
        var journal = new FileRecoveryJournal(temp.Path);
        var session = new ProjectSession(baseDocument, journal);
        var constraint = new ConstraintDefinition(
            new ConstraintId("c_test"),
            "AllowedSide",
            new ConstraintSelector("ENTITY", "COMPONENT", ["cmp_u1"], null),
            null,
            JsonSerializer.SerializeToElement(new { allowedSides = new[] { "TOP" } }),
            "REQUIRED",
            new ConstraintScope([], null, null, []),
            Provenance.UserDefined,
            "test",
            true);
        var group = new Group(new GroupId("grp_power"), "Power", "FUNCTIONAL", null, [new GroupMember("COMPONENT", "cmp_u1")], new Dictionary<string, SourcedValue>(StringComparer.Ordinal));
        var region = new Region(new RegionId("reg_power"), "Power", new Polygon2([
            new Point2(new(0), new(0)),
            new Point2(new(1000), new(0)),
            new Point2(new(1000), new(1000)),
            new Point2(new(0), new(1000))
        ], []), [], "POWER", new Dictionary<string, SourcedValue>(StringComparer.Ordinal));

        var metadata = session.BeginTransaction("metadata", "test");
        metadata.Apply(new UpdateProjectMetadataAction("Recovered", "journal"));
        session.Apply(metadata);
        var manufacturing = session.BeginTransaction("manufacturing", "test");
        manufacturing.Apply(new UpdateManufacturingCapabilityAction("minimumClearance", KnownUm(250)));
        session.Apply(manufacturing);
        var authoring = session.BeginTransaction("authoring", "test");
        authoring.Apply(new UpsertConstraintAction(constraint));
        authoring.Apply(new UpsertGroupAction(group));
        authoring.Apply(new UpsertRegionAction(region));
        session.Apply(authoring);

        var recovered = journal.Replay(baseDocument);

        Assert.Equal("Recovered", recovered.Project.Metadata.Name);
        Assert.Equal("KNOWN", recovered.Project.ManufacturingProfile.Capabilities["minimumClearance"].Status);
        Assert.Contains(recovered.Project.Constraints, c => c.Id.Value == "c_test");
        Assert.Contains(recovered.Project.LogicalDesign.Groups, g => g.Id.Value == "grp_power");
        Assert.Contains(recovered.Project.Board.Regions, r => r.Id.Value == "reg_power");
    }

    [Fact]
    public void Recovery_journal_rejects_unexpected_base_revision()
    {
        using var temp = new TempDirectory();
        var baseDocument = ImportedDocument();
        var journal = new FileRecoveryJournal(temp.Path);
        var session = new ProjectSession(baseDocument, journal);
        var transaction = session.BeginTransaction("move", "test");
        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2200), new(2200)), "test"));
        session.Apply(transaction);
        var wrongBase = baseDocument with { Project = baseDocument.Project with { ProjectRevision = 7 } };

        Assert.Throws<InvalidDataException>(() => journal.Replay(wrongBase));
    }

    [Fact]
    public void Delete_constraint_group_and_region_are_reported_in_diff_and_impact()
    {
        var session = new ProjectSession(ImportedDocument());
        var constraint = ComponentAllowedSideConstraint("c_delete", "cmp_u1");
        var group = new Group(new GroupId("grp_temp"), "Temp", "FUNCTIONAL", null, [new GroupMember("COMPONENT", "cmp_u1")], new Dictionary<string, SourcedValue>(StringComparer.Ordinal));
        var region = new Region(new RegionId("reg_temp"), "Temp", new Polygon2([
            new Point2(new(0), new(0)),
            new Point2(new(1000), new(0)),
            new Point2(new(1000), new(1000)),
            new Point2(new(0), new(1000))
        ], []), [], "TEMP", new Dictionary<string, SourcedValue>(StringComparer.Ordinal));
        var setup = session.BeginTransaction("setup", "test");
        setup.Apply(new UpsertConstraintAction(constraint));
        setup.Apply(new UpsertGroupAction(group));
        setup.Apply(new UpsertRegionAction(region));
        session.Apply(setup);

        var delete = session.BeginTransaction("delete authoring entities", "test");
        delete.Apply(new DeleteConstraintAction(constraint.Id));
        delete.Apply(new DeleteGroupAction(group.Id));
        delete.Apply(new DeleteRegionAction(region.Id));
        var diff = delete.Diff;
        var impact = new EditImpactPlanner().Plan(diff, DependencyGraph.From(session.Document.Project));

        Assert.Contains(diff.DirectChanges, r => r.EntityType == "CONSTRAINT" && r.EntityId == "c_delete");
        Assert.Contains(diff.DirectChanges, r => r.EntityType == "GROUP" && r.EntityId == "grp_temp");
        Assert.Contains(diff.DirectChanges, r => r.EntityType == "REGION" && r.EntityId == "reg_temp");
        Assert.Contains(DerivedArtifactStage.ConstraintResolution, impact.InvalidStages);
        Assert.Contains(DerivedArtifactStage.AbsoluteGeometry, impact.InvalidStages);
    }

    [Fact]
    public void Transaction_rejects_structural_corruption()
    {
        var validator = new CanonicalIntegrityValidator();
        var session = new ProjectSession(ImportedDocument(), validator: validator);
        var group = new Group(new GroupId("grp_power"), "Power", "FUNCTIONAL", null, [new GroupMember("COMPONENT", "cmp_u1")], new Dictionary<string, SourcedValue>(StringComparer.Ordinal));
        var constraint = new ConstraintDefinition(
            new ConstraintId("c_group"),
            "AllowedSide",
            new ConstraintSelector("ENTITY", "GROUP", ["grp_power"], null),
            null,
            JsonSerializer.SerializeToElement(new { allowedSides = new[] { "TOP" } }),
            "REQUIRED",
            new ConstraintScope([], null, null, []),
            Provenance.UserDefined,
            "test",
            true);
        var setup = session.BeginTransaction("setup", "test");
        setup.Apply(new UpsertGroupAction(group));
        setup.Apply(new UpsertConstraintAction(constraint));
        session.Apply(setup);

        var delete = session.BeginTransaction("delete dangling group", "test");
        delete.Apply(new DeleteGroupAction(group.Id));

        Assert.Throws<InvalidOperationException>(() => session.Apply(delete));
    }

    [Fact]
    public void Dsn_import_converts_declared_units()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "metric.dsn");
        File.WriteAllText(path, MetricDsn, Encoding.UTF8);

        var result = Service().ImportDesign(new ImportRequest(path, SourceRetentionPolicy.ReferenceOnly));

        Assert.True(result.Success, Messages(result.Diagnostics));
        Assert.Equal(10_000, result.Project!.Board.Outline!.Outer[1].X.Value);
        Assert.Equal("KNOWN", result.Project.ManufacturingProfile.Capabilities["minimumTrackWidth"].Status);
        Assert.Equal(150, result.Project.ManufacturingProfile.Capabilities["minimumTrackWidth"].Value.GetInt64());
    }

    [Fact]
    public void Dsn_import_rejects_unsupported_units()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "bad-unit.dsn");
        File.WriteAllText(path, SampleDsn.Replace("(unit um)", "(unit furlong)", StringComparison.Ordinal), Encoding.UTF8);

        var result = Service().ImportDesign(new ImportRequest(path, SourceRetentionPolicy.ReferenceOnly));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ImportInvalidSource && d.Blocking);
    }

    [Fact]
    public void Save_as_updates_session_file_context_for_followup_saves()
    {
        using var temp = new TempDirectory();
        var source = WriteDsn(temp.Path);
        var service = Service();
        var imported = service.ImportDesign(new ImportRequest(source, SourceRetentionPolicy.Embed));
        Assert.True(imported.Success, Messages(imported.Diagnostics));
        var session = new ProjectSession(imported.Document!);
        var firstPath = Path.Combine(temp.Path, "first.prdx");
        var secondPath = Path.Combine(temp.Path, "second.prdx");

        Assert.True(session.Save(new PrdxProjectStore(integrityValidator: new CanonicalIntegrityValidator()), firstPath).Success);
        Assert.Equal(Path.GetFullPath(firstPath), session.Document.FileContext.SourcePath);
        Assert.Empty(session.Document.FileContext.PendingSupplementaryFiles);
        Assert.True(session.Save(new PrdxProjectStore(integrityValidator: new CanonicalIntegrityValidator()), secondPath).Success);
        File.Delete(firstPath);
        Assert.True(session.Save(new PrdxProjectStore(integrityValidator: new CanonicalIntegrityValidator()), secondPath).Success);
        var reopened = service.LoadProject(secondPath);

        Assert.True(reopened.Success, Messages(reopened.Diagnostics));
        using var archive = ZipFile.OpenRead(secondPath);
        Assert.NotNull(archive.GetEntry("source/" + Path.GetFileName(source)));
    }

    [Fact]
    public void Run_baseline_becomes_stale_after_edit()
    {
        var session = new ProjectSession(ImportedDocument());
        var runs = new RunBaselineService();
        var run = runs.StartRun(session);

        var before = runs.CanCommit(run, session);
        Assert.True(before.CanCommit);

        var transaction = session.BeginTransaction("move", "test");
        transaction.Apply(new MoveComponentAction(new ComponentId("cmp_u1"), new Point2(new(2300), new(2300)), "test"));
        session.Apply(transaction);

        var after = runs.CanCommit(run, session);
        Assert.False(after.CanCommit);
        Assert.Equal(DiagnosticCodes.RunBaselineStale, after.Diagnostic!.Code);
    }

    [Fact]
    public void Cli_import_dsn_and_project_check_are_testable_in_process()
    {
        using var temp = new TempDirectory();
        var source = WriteDsn(temp.Path);
        var output = Path.Combine(temp.Path, "cli.prdx");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var cli = new CliApplication(Service(), stdout, stderr, "test");

        Assert.Equal(0, cli.Run(["import-dsn", source, "--out", output, "--embed-source"]));
        Assert.True(File.Exists(output));

        stdout.GetStringBuilder().Clear();
        Assert.Equal(0, cli.Run(["project-check", output, "--json"]));
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("Components").GetInt32());
    }

    private static ProjectService Service()
    {
        var validator = new CanonicalIntegrityValidator();
        return new ProjectService(new PrdxProjectStore(integrityValidator: validator), validator, [new SpecctraDsnImporter()]);
    }

    private static ProjectDocument ImportedDocument()
    {
        using var temp = new TempDirectory();
        var result = Service().ImportDesign(new ImportRequest(WriteDsn(temp.Path), SourceRetentionPolicy.ReferenceOnly));
        Assert.True(result.Success, Messages(result.Diagnostics));
        return result.Document!;
    }

    private static string WriteDsn(string directory)
    {
        var path = Path.Combine(directory, "sample-plan03.dsn");
        File.WriteAllText(path, SampleDsn, Encoding.UTF8);
        return path;
    }

    private static string SourceHash(string source) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();

    private static ComponentPose Pose(CanonicalProject project, string componentId) =>
        project.PhysicalDesignState.ComponentPoses.Single(p => p.ComponentId.Value == componentId);

    private static SourcedValue KnownUm(long value) =>
        new(JsonSerializer.SerializeToElement(value), "um", "KNOWN", 1.0, Provenance.UserDefined);

    private static ConstraintDefinition ComponentAllowedSideConstraint(string id, string componentId) =>
        new(
            new ConstraintId(id),
            "AllowedSide",
            new ConstraintSelector("ENTITY", "COMPONENT", [componentId], null),
            null,
            JsonSerializer.SerializeToElement(new { allowedSides = new[] { "TOP" } }),
            "REQUIRED",
            new ConstraintScope([], null, null, []),
            Provenance.UserDefined,
            "test",
            true);

    private static string Messages(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    private const string SampleDsn = """
(pcb DemoBoard
  (unit um)
  (structure
    (layer Top signal)
    (layer Bottom signal)
    (boundary (path pcb 0 0 10000 0 10000 6000 0 6000))
  )
  (placement
    (component U1 (footprint R_0603) (place 1000 1000 front 0)
      (pad 1 smd rect 0 0 600 800 Top)
      (pad 2 smd rect 1200 0 600 800 Top))
    (component U2 (footprint R_0603) (place 5000 1000 front 180)
      (pad 1 smd rect 0 0 600 800 Top)
      (pad 2 smd rect 1200 0 600 800 Top))
  )
  (network
    (net N1 (pins U1-1 U2-1))
    (net GND (pins U1-2 U2-2))
  )
  (rules (width 150) (clearance 150) (drill 300) (via 600))
)
""";

    private const string PartialDsn = """
(pcb PartialBoard
  (unit um)
  (placement
    (component U1)
  )
  (network
    (net N1 (pins U1-1))
  )
)
""";

    private const string MetricDsn = """
(pcb MetricBoard
  (unit mm)
  (structure
    (layer Top signal)
    (layer Bottom signal)
    (boundary (path pcb 0 0 10 0 10 6 0 6))
  )
  (placement
    (component U1 (footprint R_0603) (place 1 1 front 0)
      (pad 1 smd rect 0 0 0.6 0.8 Top)
      (pad 2 smd rect 1.2 0 0.6 0.8 Top))
  )
  (network
    (net N1 (pins U1-1))
  )
  (rules (width 0.15) (clearance 0.15))
)
""";
}
