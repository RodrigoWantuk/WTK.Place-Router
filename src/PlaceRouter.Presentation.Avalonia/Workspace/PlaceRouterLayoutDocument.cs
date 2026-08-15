namespace PlaceRouter.Presentation.Workspace;

public sealed class PlaceRouterLayoutDocument
{
    public string Language { get; set; } = "pt-BR";

    public PlaceRouterLayoutState Layout { get; set; } = new();

    public List<string> RecentProjects { get; set; } = [];
}

public sealed class PlaceRouterLayoutState
{
    public double LeftProportion { get; set; } = 0.22;

    public double RightProportion { get; set; } = 0.26;

    public double BottomProportion { get; set; } = 0.28;

    public double ComposerProportion { get; set; } = 0.42;

    public double InspectorProportion { get; set; } = 0.58;

    public List<PlaceRouterFloatingDockState> FloatingDocks { get; set; } = [];
}

public sealed class PlaceRouterFloatingDockState
{
    public string ToolId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 520;
    public string MonitorId { get; set; } = string.Empty;
}
