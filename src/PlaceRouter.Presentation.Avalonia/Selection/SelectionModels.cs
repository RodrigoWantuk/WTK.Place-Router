using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.Presentation.Selection;

public enum SelectionOrigin
{
    Unknown,
    Navigator,
    Viewport,
    Inspector,
    Workbench,
    Command
}

public sealed record SelectionState(IReadOnlyList<EntityReference> Items, SelectionOrigin Origin)
{
    public static SelectionState Empty { get; } = new([], SelectionOrigin.Unknown);

    public EntityReference? Primary => Items.FirstOrDefault();
}
