using PlaceRouter.Core.Diagnostics;

namespace PlaceRouter.Presentation.Selection;

public sealed class SelectionCoordinator
{
    private SelectionState _current = SelectionState.Empty;

    public event EventHandler<SelectionState>? SelectionChanged;

    public SelectionState Current => _current;

    public void Select(EntityReference reference, SelectionOrigin origin) =>
        Select([reference], origin);

    public void Select(IEnumerable<EntityReference> references, SelectionOrigin origin)
    {
        var normalized = references
            .Where(static item => !string.IsNullOrWhiteSpace(item.EntityType) && !string.IsNullOrWhiteSpace(item.EntityId))
            .GroupBy(static item => $"{item.EntityType}:{item.EntityId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var next = new SelectionState(normalized, origin);
        if (SameSelection(_current, next))
        {
            return;
        }

        _current = next;
        SelectionChanged?.Invoke(this, _current);
    }

    public void Clear(SelectionOrigin origin) => Select([], origin);

    private static bool SameSelection(SelectionState first, SelectionState second) =>
        first.Items.Count == second.Items.Count &&
        first.Items.Zip(second.Items).All(pair =>
            string.Equals(pair.First.EntityType, pair.Second.EntityType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pair.First.EntityId, pair.Second.EntityId, StringComparison.Ordinal));
}
