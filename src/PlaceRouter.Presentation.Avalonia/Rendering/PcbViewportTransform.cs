using Avalonia;
using PlaceRouter.Geometry;

namespace PlaceRouter.Presentation.Rendering;

public sealed class PcbViewportTransform
{
    public double Scale { get; private set; } = 1;

    public Vector Offset { get; private set; }

    public void Fit(GeometryEnvelope bounds, Size viewport, double padding = 32)
    {
        if (!bounds.IsValid || viewport.Width <= padding * 2 || viewport.Height <= padding * 2)
        {
            Scale = 1;
            Offset = default;
            return;
        }

        var scaleX = (viewport.Width - padding * 2) / Math.Max(1, bounds.Width);
        var scaleY = (viewport.Height - padding * 2) / Math.Max(1, bounds.Height);
        Scale = Math.Max(0.0001, Math.Min(scaleX, scaleY));
        var centerWorld = new Point((bounds.MinX + bounds.MaxX) / 2.0, (bounds.MinY + bounds.MaxY) / 2.0);
        var centerScreen = new Point(viewport.Width / 2.0, viewport.Height / 2.0);
        Offset = centerScreen - new Point(centerWorld.X * Scale, centerWorld.Y * Scale);
    }

    public void Pan(Vector delta) => Offset += delta;

    public void ZoomAt(Point screenPoint, double factor)
    {
        var before = ScreenToWorld(screenPoint);
        Scale = Math.Clamp(Scale * factor, 0.00002, 2);
        var afterScreen = WorldToScreen(before);
        Offset += screenPoint - afterScreen;
    }

    public Point WorldToScreen(GeometryPoint point) =>
        new(point.X * Scale + Offset.X, point.Y * Scale + Offset.Y);

    public GeometryPoint ScreenToWorld(Point point) =>
        new((long)Math.Round((point.X - Offset.X) / Scale), (long)Math.Round((point.Y - Offset.Y) / Scale));
}
