using System.Text;
using Avalonia;
using PlaceRouter.Application.Projects;
using PlaceRouter.Core.Diagnostics;
using PlaceRouter.Core.Primitives;
using PlaceRouter.DesignExchange.Prdx;
using PlaceRouter.DesignExchange.Specctra;
using PlaceRouter.Domain.Model;
using PlaceRouter.Geometry;
using PlaceRouter.Presentation.Docking;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.Rendering;
using PlaceRouter.Presentation.Selection;
using PlaceRouter.Presentation.Workspace;

namespace PlaceRouter.Presentation.Tests;

public sealed class Plan04DesktopShellTests
{
    [Fact]
    public void ProjectCoordinator_imports_saves_and_reopens_real_project()
    {
        using var temp = new TempDirectory();
        var coordinator = new ProjectCoordinator(Service());

        var imported = coordinator.ImportDesign(WriteDsn(temp.Path), SourceRetentionPolicy.ReferenceOnly);
        Assert.True(imported.Success, Messages(imported.Diagnostics));
        Assert.NotNull(coordinator.Project);
        Assert.NotNull(coordinator.ConstraintReport);
        Assert.True(coordinator.IsDirty);

        var path = Path.Combine(temp.Path, "demo.prdx");
        var saved = coordinator.SaveProjectAs(path);
        Assert.True(saved.Success, Messages(saved.Diagnostics));
        Assert.False(coordinator.IsDirty);

        coordinator.CloseProject();
        var opened = coordinator.OpenProject(path);
        Assert.True(opened.Success, Messages(opened.Diagnostics));
        Assert.Equal("DemoBoard", coordinator.Project!.Metadata.Name);
        Assert.Contains(path, coordinator.RecentProjects);
    }

    [Fact]
    public void SelectionCoordinator_deduplicates_and_suppresses_noop_notifications()
    {
        var coordinator = new SelectionCoordinator();
        var calls = 0;
        coordinator.SelectionChanged += (_, _) => calls++;

        coordinator.Select([
            new EntityReference("COMPONENT", "cmp_u1"),
            new EntityReference("COMPONENT", "cmp_u1")
        ], SelectionOrigin.Navigator);
        coordinator.Select(new EntityReference("COMPONENT", "cmp_u1"), SelectionOrigin.Viewport);

        Assert.Equal(1, calls);
        Assert.Single(coordinator.Current.Items);
    }

    [Fact]
    public void LayoutState_normalizes_floating_tool_inside_available_monitor()
    {
        var state = new PlaceRouterFloatingDockState
        {
            ToolId = "tool.inspector",
            MonitorId = "missing",
            X = 10_000,
            Y = -500,
            Width = 8_000,
            Height = 8_000
        };

        var normalized = PlaceRouterDockLayoutState.Normalize(state, [
            new PlaceRouterMonitorWorkArea("primary", new Rect(0, 0, 1280, 720), true)
        ]);

        Assert.Equal("primary", normalized.MonitorId);
        Assert.InRange(normalized.X, 0, 1280 - normalized.Width);
        Assert.InRange(normalized.Y, 0, 720 - normalized.Height);
        Assert.InRange(normalized.Width, 280, 1280);
        Assert.InRange(normalized.Height, 220, 720);
    }

    [Fact]
    public void Viewport_snapshot_hit_test_returns_topmost_physical_entity()
    {
        var board = new PcbShapeSnapshot(
            PcbShapeKind.Board,
            "BOARD",
            "board",
            Rect(0, 0, 10_000, 6_000),
            null,
            null,
            "Board",
            "normal");
        var component = new PcbShapeSnapshot(
            PcbShapeKind.Component,
            "COMPONENT",
            "cmp_u1",
            Rect(900, 900, 1_900, 1_700),
            null,
            null,
            "U1",
            "normal");
        var pad = new PcbShapeSnapshot(
            PcbShapeKind.Pad,
            "PAD",
            "cmp_u1:pad_1",
            Rect(1_000, 1_000, 1_300, 1_300),
            "layer_top_cu",
            "net_n1",
            "Pad",
            "normal");
        var snapshot = new PcbBoardSnapshot(new GeometryEnvelope(0, 0, 10_000, 6_000), [board, component, pad], [], [], []);

        var hit = snapshot.HitTest(new GeometryPoint(1_100, 1_100));

        Assert.NotNull(hit);
        Assert.Equal(PcbShapeKind.Pad, hit!.Kind);
        Assert.Equal("cmp_u1:pad_1", hit.EntityId);
    }

    [Fact]
    public void Snapshot_builder_uses_unique_physical_pad_instance_ids()
    {
        using var temp = new TempDirectory();
        var result = Service().ImportDesign(new ImportRequest(WriteDsn(temp.Path), SourceRetentionPolicy.ReferenceOnly));
        Assert.True(result.Success, Messages(result.Diagnostics));

        var snapshot = new PcbSnapshotBuilder().Build(result.Project, null, []);
        var pads = snapshot.Shapes.Where(s => s.Kind == PcbShapeKind.Pad).ToArray();

        Assert.Equal(4, pads.Length);
        Assert.Equal(4, pads.Select(p => p.EntityId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(pads, p => p.EntityId.StartsWith("cmp_u1:pad:", StringComparison.Ordinal));
        Assert.Contains(pads, p => p.EntityId.StartsWith("cmp_u2:pad:", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectCoordinator_uses_session_dirty_state_and_save_clears_it()
    {
        using var temp = new TempDirectory();
        var coordinator = new ProjectCoordinator(Service());

        coordinator.ImportDesign(WriteDsn(temp.Path), SourceRetentionPolicy.ReferenceOnly);
        Assert.True(coordinator.IsDirty);
        Assert.True(coordinator.Session!.IsDirty);

        var saved = coordinator.SaveProjectAs(Path.Combine(temp.Path, "saved.prdx"));

        Assert.True(saved.Success, Messages(saved.Diagnostics));
        Assert.False(coordinator.IsDirty);
        Assert.False(coordinator.Session!.IsDirty);
    }

    private static ProjectService Service()
    {
        var validator = new CanonicalIntegrityValidator();
        return new ProjectService(new PrdxProjectStore(integrityValidator: validator), validator, [new SpecctraDsnImporter()]);
    }

    private static string WriteDsn(string directory)
    {
        var path = Path.Combine(directory, "sample-plan04.dsn");
        File.WriteAllText(path, SampleDsn, Encoding.UTF8);
        return path;
    }

    private static GeometryPolygon Rect(long x1, long y1, long x2, long y2) =>
        new(
            [new GeometryPoint(x1, y1), new GeometryPoint(x2, y1), new GeometryPoint(x2, y2), new GeometryPoint(x1, y2)],
            []);

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

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "placerouter-plan04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
