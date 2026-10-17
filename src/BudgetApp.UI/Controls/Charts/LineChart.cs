using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using BudgetApp.Core.Formatting;

namespace BudgetApp.UI.Controls.Charts;

// Smooth area chart for the payday forecast. Anything below zero is drawn in coral.
public class LineChart : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<ChartPoint>?> ItemsProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartPoint>?>(nameof(Items));

    public static readonly StyledProperty<string> CurrencyProperty =
        AvaloniaProperty.Register<LineChart, string>(nameof(Currency), "ZAR");

    private const double AxisWidth = 56;
    private const double LabelHeight = 24;

    private int _hover = -1;
    private double _crosshairX = double.NaN;
    private double _crosshairOpacity;

    public IReadOnlyList<ChartPoint>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Currency
    {
        get => GetValue(CurrencyProperty);
        set => SetValue(CurrencyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            _hover = -1;
            if (VisualRoot is not null)
                Replay();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var items = Items;
        var plot = PlotArea();
        if (items is null || items.Count < 2 || plot.Width <= 0)
            return;

        var x = Math.Clamp(e.GetPosition(this).X, plot.Left, plot.Right);
        var index = (int)Math.Round((x - plot.Left) / plot.Width * (items.Count - 1));
        if (index != _hover)
        {
            _hover = index;
            if (double.IsNaN(_crosshairX))
                _crosshairX = XAt(index, items.Count, plot);
            Animate();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        Animate();
    }

    protected override bool Step(double seconds)
    {
        var items = Items;
        var visible = _hover >= 0 && items is { Count: > 1 };
        _crosshairOpacity = Approach(_crosshairOpacity, visible ? 1 : 0, seconds, 12);

        if (visible)
        {
            var target = XAt(_hover, items!.Count, PlotArea());
            _crosshairX = double.IsNaN(_crosshairX) ? target : Approach(_crosshairX, target, seconds, 22);
            return _crosshairX != target || _crosshairOpacity != 1;
        }

        if (_crosshairOpacity == 0)
            _crosshairX = double.NaN;
        return _crosshairOpacity != 0;
    }

    public override void Render(DrawingContext context)
    {
        var items = Items ?? [];
        var plot = PlotArea();
        if (plot.Width <= 0 || plot.Height <= 0 || items.Count < 2)
            return;

        var hasBand = items.Any(p => p.Low is not null && p.High is not null);
        var (minValue, maxValue) = AxisRange(
            items.Min(p => Math.Min(p.Value, p.Low ?? p.Value)),
            items.Max(p => Math.Max(p.Value, p.High ?? p.Value)));
        var range = maxValue - minValue;
        double YAt(double v) => plot.Bottom - (v - minValue) / range * plot.Height;

        var captionSize = Resource("FontSizeCaption", 12);
        var secondary = Brush("TextSecondaryBrush");
        var grid = new Pen(Brush("BorderBrush"), 1, new DashStyle([4, 4], 0));

        foreach (var value in new[] { minValue, minValue + range / 2, maxValue }.Distinct())
        {
            var y = YAt(value);
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(MoneyFormatter.FormatCompact((decimal)value, Currency), captionSize, secondary);
            context.DrawText(label, new Point(plot.Left - label.Width - 10, y - label.Height / 2));
        }

        var zeroY = YAt(0);
        if (minValue < 0)
            context.DrawLine(new Pen(Brush("NegativeBrush"), 1, new DashStyle([2, 3], 0)), new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));

        foreach (var index in new[] { 0, items.Count / 2, items.Count - 1 }.Distinct())
        {
            var label = Text(items[index].Label, captionSize, secondary);
            var x = Math.Clamp(XAt(index, items.Count, plot) - label.Width / 2, plot.Left, Math.Max(plot.Left, plot.Right - label.Width));
            context.DrawText(label, new Point(x, plot.Bottom + 6));
        }

        var points = items.Select((p, i) => new Point(XAt(i, items.Count, plot), YAt(p.Value))).ToList();
        var line = SmoothPath(points, close: false, plot.Bottom);
        var area = SmoothPath(points, close: true, zeroY);

        var accent = Brush("AccentBrush");
        var accentColour = (accent as ISolidColorBrush)?.Color ?? Colors.MediumSeaGreen;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(70, accentColour.R, accentColour.G, accentColour.B), 0),
                new GradientStop(Color.FromArgb(0, accentColour.R, accentColour.G, accentColour.B), 1),
            },
        };

        var reveal = new Rect(plot.Left - 6, 0, (plot.Width + 12) * Progress, Bounds.Height);
        using (context.PushClip(reveal))
        {
            // Likely range: a soft band between the low and high estimates.
            if (hasBand)
            {
                var band = new StreamGeometry();
                using (var ctx = band.Open())
                {
                    ctx.BeginFigure(new Point(points[0].X, YAt(items[0].High ?? items[0].Value)), true);
                    for (var i = 1; i < items.Count; i++)
                        ctx.LineTo(new Point(points[i].X, YAt(items[i].High ?? items[i].Value)));
                    for (var i = items.Count - 1; i >= 0; i--)
                        ctx.LineTo(new Point(points[i].X, YAt(items[i].Low ?? items[i].Value)));
                    ctx.EndFigure(true);
                }

                context.DrawGeometry(new SolidColorBrush(accentColour, 0.14), null, band);
            }

            using (context.PushClip(new Rect(plot.Left, plot.Top - 8, plot.Width, Math.Max(0, zeroY - plot.Top + 8))))
            {
                context.DrawGeometry(fill, null, area);
                context.DrawGeometry(null, new Pen(accent, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            }

            if (minValue < 0)
            {
                using (context.PushClip(new Rect(plot.Left, zeroY, plot.Width, plot.Bottom - zeroY + 8)))
                    context.DrawGeometry(null, new Pen(Brush("NegativeBrush"), 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Marker is null)
                    continue;
                var brush = items[i].IsWarning ? Brush("WarningBrush") : accent;
                context.DrawEllipse(Brush("BackgroundBrush"), new Pen(brush, 2), points[i], 4.5, 4.5);
            }
        }

        if (_crosshairOpacity > 0.01 && !double.IsNaN(_crosshairX) && _hover >= 0 && _hover < items.Count)
        {
            using (context.PushOpacity(_crosshairOpacity))
            {
                context.DrawLine(new Pen(Brush("BorderBrush"), 1), new Point(_crosshairX, plot.Top), new Point(_crosshairX, plot.Bottom));
                var p = points[_hover];
                var dotBrush = items[_hover].Value < 0 ? Brush("NegativeBrush") : accent;
                context.DrawEllipse(dotBrush, new Pen(Brush("BackgroundBrush"), 2), new Point(_crosshairX, p.Y), 5, 5);
                var title = items[_hover].Marker is { } marker ? $"{items[_hover].Label} · {marker}" : items[_hover].Label;
                DrawTooltip(context, new Point(_crosshairX, p.Y), title, items[_hover].ValueText);
            }
        }
    }

    // Zoom to the data when the balance stays positive (a zero-based axis would flatten the line),
    // but keep zero in view whenever the forecast dips below it.
    private static (double Min, double Max) AxisRange(double low, double high)
    {
        var span = Math.Max(high - low, Math.Abs(high) * 0.1);
        if (span <= 0)
            span = 1000;

        var step = NiceStep(span / 3);
        if (low >= 0)
        {
            var min = Math.Max(0, Math.Floor((low - span * 0.4) / step) * step);
            return (min, Math.Ceiling((high + span * 0.15) / step) * step);
        }

        return (Math.Floor((low - span * 0.1) / step) * step, Math.Ceiling(Math.Max(high + span * 0.1, step) / step) * step);
    }

    private static double NiceStep(double raw)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var factor in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (factor * magnitude >= raw)
                return factor * magnitude;
        }

        return 10 * magnitude;
    }

    private Rect PlotArea() => new(AxisWidth, 10, Math.Max(0, Bounds.Width - AxisWidth - 8), Math.Max(0, Bounds.Height - LabelHeight - 10));

    private static double XAt(int index, int count, Rect plot) => plot.Left + plot.Width * index / Math.Max(1, count - 1);

    // Monotone-ish smoothing: Catmull-Rom tangents scaled down so the curve doesn't overshoot much.
    private static StreamGeometry SmoothPath(IReadOnlyList<Point> points, bool close, double baseline)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(close ? new Point(points[0].X, baseline) : points[0], close);
        if (close)
            ctx.LineTo(points[0]);

        const double tension = 0.16;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(points.Count - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) * tension, p1.Y + (p2.Y - p0.Y) * tension);
            var c2 = new Point(p2.X - (p3.X - p1.X) * tension, p2.Y - (p3.Y - p1.Y) * tension);
            ctx.CubicBezierTo(c1, c2, p2);
        }

        if (close)
            ctx.LineTo(new Point(points[^1].X, baseline));
        ctx.EndFigure(close);
        return geometry;
    }
}
