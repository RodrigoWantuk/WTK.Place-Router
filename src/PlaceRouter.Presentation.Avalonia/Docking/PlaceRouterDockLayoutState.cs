using Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using PlaceRouter.Presentation.Workspace;

namespace PlaceRouter.Presentation.Docking;

public sealed record PlaceRouterMonitorWorkArea(string Id, Rect Bounds, bool IsPrimary = false);

public static class PlaceRouterDockLayoutState
{
    private static readonly PlaceRouterMonitorWorkArea VirtualPrimary =
        new("virtual-primary", new Rect(0, 0, 1920, 1080), true);

    public static IReadOnlyList<PlaceRouterFloatingDockState> Capture(
        IRootDock root,
        IReadOnlyList<PlaceRouterMonitorWorkArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(root);
        var available = NormalizeMonitors(monitors);
        return (root.Windows ?? [])
            .Select(window => CaptureWindow(window, available))
            .Where(static state => state is not null)
            .Select(static state => state!)
            .GroupBy(static state => state.ToolId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    public static PlaceRouterFloatingDockState Normalize(
        PlaceRouterFloatingDockState state,
        IReadOnlyList<PlaceRouterMonitorWorkArea> monitors)
    {
        var available = NormalizeMonitors(monitors);
        var monitor = available.FirstOrDefault(item => item.Id == state.MonitorId)
            ?? available.FirstOrDefault(static item => item.IsPrimary)
            ?? available[0];
        var width = ClampSize(state.Width, 280, Math.Max(280, monitor.Bounds.Width), 420);
        var height = ClampSize(state.Height, 220, Math.Max(220, monitor.Bounds.Height), 520);
        var x = ClampFinite(state.X, monitor.Bounds.Left, monitor.Bounds.Right - width, monitor.Bounds.Left + 80);
        var y = ClampFinite(state.Y, monitor.Bounds.Top, monitor.Bounds.Bottom - height, monitor.Bounds.Top + 80);
        return new PlaceRouterFloatingDockState
        {
            ToolId = state.ToolId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MonitorId = monitor.Id
        };
    }

    private static PlaceRouterFloatingDockState? CaptureWindow(IDockWindow window, IReadOnlyList<PlaceRouterMonitorWorkArea> monitors)
    {
        var tool = EnumerateDockables(window.Layout).OfType<Tool>().FirstOrDefault();
        if (tool?.Id is null)
        {
            return null;
        }

        var center = new Point(window.X + window.Width / 2, window.Y + window.Height / 2);
        var monitor = monitors.FirstOrDefault(item => item.Bounds.Contains(center))
            ?? monitors.FirstOrDefault(static item => item.IsPrimary)
            ?? monitors[0];
        return Normalize(new PlaceRouterFloatingDockState
        {
            ToolId = tool.Id,
            X = window.X,
            Y = window.Y,
            Width = window.Width,
            Height = window.Height,
            MonitorId = monitor.Id
        }, monitors);
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable? dockable)
    {
        if (dockable is null)
        {
            yield break;
        }

        yield return dockable;
        if (dockable is not IDock dock || dock.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables)
        {
            foreach (var descendant in EnumerateDockables(child))
            {
                yield return descendant;
            }
        }
    }

    private static IReadOnlyList<PlaceRouterMonitorWorkArea> NormalizeMonitors(IReadOnlyList<PlaceRouterMonitorWorkArea>? monitors) =>
        monitors is { Count: > 0 } ? monitors : [VirtualPrimary];

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value) || maximum < minimum)
        {
            return fallback;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static double ClampSize(double value, double minimum, double maximum, double fallback) =>
        !double.IsFinite(value) || value <= 0 ? fallback : Math.Clamp(value, minimum, maximum);
}
