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
}
