using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PlaceRouter.Presentation.Rendering;
using Geometry = PlaceRouter.Geometry;

namespace PlaceRouter.Presentation.Views;

public sealed class PcbViewportControl : Control
{
    public static readonly StyledProperty<PcbBoardSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<PcbViewportControl, PcbBoardSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<ICommand?> EntitySelectedCommandProperty =
        AvaloniaProperty.Register<PcbViewportControl, ICommand?>(nameof(EntitySelectedCommand));

    private readonly PcbViewportTransform _transform = new();
    private Point? _lastPanPoint;

    public PcbBoardSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public ICommand? EntitySelectedCommand
    {
        get => GetValue(EntitySelectedCommandProperty);
        set => SetValue(EntitySelectedCommandProperty, value);
    }

    static PcbViewportControl()
    {
        AffectsRender<PcbViewportControl>(SnapshotProperty);
        FocusableProperty.OverrideDefaultValue<PcbViewportControl>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == SnapshotProperty)
        {
            Fit();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _transform.ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.12 : 0.88);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed || properties.IsRightButtonPressed)
        {
            _lastPanPoint = point;
            e.Pointer.Capture(this);
            return;
        }

        if (Snapshot is { } snapshot)
        {
            var shape = snapshot.HitTest(_transform.ScreenToWorld(point));
            if (EntitySelectedCommand?.CanExecute(shape) == true)
            {
                EntitySelectedCommand.Execute(shape);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPanPoint is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        _transform.Pan(point - _lastPanPoint.Value);
        _lastPanPoint = point;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPanPoint = null;
        e.Pointer.Capture(null);
    }

    public void Fit()
    {
        if (Snapshot is { Bounds.IsValid: true })
        {
            _transform.Fit(Snapshot.Bounds, Bounds.Size);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(20, 24, 28)), Bounds);
        DrawGrid(context);

        var snapshot = Snapshot;
        if (snapshot is null || snapshot.Shapes.Count == 0)
        {
            DrawCenteredText(context, "Nenhum projeto carregado");
            return;
        }

        foreach (var edge in snapshot.Ratsnest)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 230, 190, 90)), 1), _transform.WorldToScreen(edge.From), _transform.WorldToScreen(edge.To));
        }

        foreach (var shape in snapshot.Shapes.OrderBy(static s => DrawOrder(s.Kind)))
        {
            DrawShape(context, shape, IsSelected(snapshot, shape));
        }
    }

    private void DrawGrid(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), 1);
        const double step = 50;
        for (var x = 0d; x < Bounds.Width; x += step)
        {
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }

        for (var y = 0d; y < Bounds.Height; y += step)
        {
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawShape(DrawingContext context, PcbShapeSnapshot shape, bool selected)
    {
        var brush = BrushFor(shape);
        var pen = new Pen(selected ? Brushes.White : StrokeFor(shape), selected ? 2.5 : 1);
        var geometry = ToGeometry(shape.Geometry);
        if (geometry is not null)
        {
            context.DrawGeometry(brush, pen, geometry);
        }

        foreach (var hole in shape.Geometry.Holes.Where(static h => h.Count >= 3))
        {
            var holeGeometry = ToGeometry(new Geometry.GeometryPolygon(hole, []));
            if (holeGeometry is not null)
            {
                context.DrawGeometry(new SolidColorBrush(Color.FromRgb(20, 24, 28)), pen, holeGeometry);
            }
        }

        if (selected)
        {
            var rect = EnvelopeRect(shape.Geometry.Envelope);
            context.DrawRectangle(null, new Pen(Brushes.White, 1), rect);
        }
    }

    private StreamGeometry? ToGeometry(Geometry.GeometryPolygon polygon)
    {
        var points = polygon.Outer;
        if (points.Count < 3)
        {
            return null;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_transform.WorldToScreen(points[0]), true);
            foreach (var point in points.Skip(1))
            {
                ctx.LineTo(_transform.WorldToScreen(point));
            }

            ctx.EndFigure(true);

            foreach (var hole in polygon.Holes.Where(static h => h.Count >= 3))
            {
                ctx.BeginFigure(_transform.WorldToScreen(hole[0]), true);
                foreach (var point in hole.Skip(1))
                {
                    ctx.LineTo(_transform.WorldToScreen(point));
                }

                ctx.EndFigure(true);
            }
        }

        return geometry;
    }

    private Rect EnvelopeRect(Geometry.GeometryEnvelope envelope)
    {
        var topLeft = _transform.WorldToScreen(new Geometry.GeometryPoint(envelope.MinX, envelope.MinY));
        var bottomRight = _transform.WorldToScreen(new Geometry.GeometryPoint(envelope.MaxX, envelope.MaxY));
        return new Rect(topLeft, bottomRight);
    }

    private static bool IsSelected(PcbBoardSnapshot snapshot, PcbShapeSnapshot shape) =>
        snapshot.Selected.Any(item =>
            item.EntityType.Equals(shape.EntityType, StringComparison.OrdinalIgnoreCase) &&
            item.EntityId.Equals(shape.EntityId, StringComparison.Ordinal));

    private static IBrush BrushFor(PcbShapeSnapshot shape) =>
        shape.Status == "violation"
            ? new SolidColorBrush(Color.FromArgb(185, 224, 72, 72))
            : shape.Kind switch
            {
                PcbShapeKind.Board => new SolidColorBrush(Color.FromArgb(50, 95, 150, 120)),
                PcbShapeKind.Component => new SolidColorBrush(Color.FromArgb(180, 70, 105, 150)),
                PcbShapeKind.Pad => new SolidColorBrush(Color.FromArgb(220, 210, 155, 72)),
                PcbShapeKind.Track => new SolidColorBrush(Color.FromArgb(210, 198, 92, 64)),
                PcbShapeKind.Via => new SolidColorBrush(Color.FromArgb(220, 185, 80, 155)),
                PcbShapeKind.CopperZone => new SolidColorBrush(Color.FromArgb(96, 198, 92, 64)),
                PcbShapeKind.Keepout => new SolidColorBrush(Color.FromArgb(80, 230, 170, 65)),
                _ => new SolidColorBrush(Color.FromArgb(90, 170, 170, 170))
            };

    private static IBrush StrokeFor(PcbShapeSnapshot shape) =>
        shape.Kind == PcbShapeKind.Board ? Brushes.SeaGreen : Brushes.Black;

    private static int DrawOrder(PcbShapeKind kind) =>
        kind switch
        {
            PcbShapeKind.Board => 0,
            PcbShapeKind.CopperZone => 10,
            PcbShapeKind.Keepout => 20,
            PcbShapeKind.Track => 30,
            PcbShapeKind.Via => 40,
            PcbShapeKind.Component => 50,
            PcbShapeKind.Pad => 60,
            _ => 70
        };

    private void DrawCenteredText(DrawingContext context, string text)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            18,
            new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)));
        context.DrawText(formatted, new Point((Bounds.Width - formatted.Width) / 2, (Bounds.Height - formatted.Height) / 2));
    }
}
