using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace BudgetApp.UI.Controls.Charts;

public class DonutChart : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<ChartSlice>?> ItemsProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<ChartSlice>?>(nameof(Items));

    public static readonly StyledProperty<string?> CenterLabelProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterLabel));

    public static readonly StyledProperty<string?> CenterValueProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(CenterValue));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<DonutChart, double>(nameof(Thickness), 26);

    public static readonly StyledProperty<ChartSlice?> HoveredSliceProperty =
        AvaloniaProperty.Register<DonutChart, ChartSlice?>(nameof(HoveredSlice), defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);

    private const double GapDegrees = 1.6;
    private const double HoverGrow = 6;

    private double[] _expansion = [];
    private int _hover = -1;

    static DonutChart()
    {
        AffectsRender<DonutChart>(CenterLabelProperty, CenterValueProperty, ThicknessProperty);
    }

    public IReadOnlyList<ChartSlice>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? CenterLabel
    {
        get => GetValue(CenterLabelProperty);
        set => SetValue(CenterLabelProperty, value);
    }

    public string? CenterValue
    {
        get => GetValue(CenterValueProperty);
        set => SetValue(CenterValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public ChartSlice? HoveredSlice
    {
        get => GetValue(HoveredSliceProperty);
        set => SetValue(HoveredSliceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            _expansion = new double[Items?.Count ?? 0];
            SetHover(-1);
            if (VisualRoot is not null)
                Replay();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(IndexAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    protected override bool Step(double seconds)
    {
        var moving = false;
        for (var i = 0; i < _expansion.Length; i++)
        {
            var target = i == _hover ? 1 : 0;
            _expansion[i] = Approach(_expansion[i], target, seconds);
            moving |= _expansion[i] != target;
        }

        return moving;
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = size / 2 - HoverGrow;
        var inner = Math.Max(0, outer - Thickness);
        var items = Items ?? [];
        var total = items.Sum(i => Math.Max(0, i.Value));

        if (total <= 0)
        {
            context.DrawGeometry(null, new Pen(Brush("BorderBrush"), Thickness), new EllipseGeometry(new Rect(
                center.X - (outer + inner) / 2, center.Y - (outer + inner) / 2, outer + inner, outer + inner)));
        }
        else
        {
            var sweepTotal = 360 * Progress;
            var gap = items.Count > 1 ? GapDegrees : 0;
            var angle = -90.0;

            for (var i = 0; i < items.Count; i++)
            {
                var sweep = Math.Max(0, items[i].Value) / total * sweepTotal;
                var drawn = Math.Max(0, sweep - gap);
                if (drawn > 0.05)
                {
                    var grow = i < _expansion.Length ? _expansion[i] * HoverGrow : 0;
                    var brush = TokenBrush(items[i].ColourToken);
                    if (_hover >= 0 && i != _hover)
                        brush = WithOpacity(brush, 0.4);
                    context.DrawGeometry(brush, null, Segment(center, outer + grow, inner - grow * 0.4, angle + gap / 2, drawn));
                }

                angle += sweep;
            }
        }

        var hovered = _hover >= 0 && _hover < items.Count ? items[_hover] : null;
        DrawCentre(context, center, hovered?.Label ?? CenterLabel, hovered?.ValueText ?? CenterValue);
    }

    private void DrawCentre(DrawingContext context, Point center, string? label, string? value)
    {
        var valueText = Text(value ?? "", Resource("FontSizeTitle", 20), Brush("TextPrimaryBrush"), FontWeight.SemiBold);
        var labelText = Text(label ?? "", Resource("FontSizeCaption", 12), Brush("TextSecondaryBrush"));
        var height = valueText.Height + labelText.Height;
        var opacity = Math.Clamp(Progress * 1.4 - 0.2, 0, 1);

        using (context.PushOpacity(opacity))
        {
            context.DrawText(labelText, new Point(center.X - labelText.Width / 2, center.Y - height / 2));
            context.DrawText(valueText, new Point(center.X - valueText.Width / 2, center.Y - height / 2 + labelText.Height));
        }
    }

    private int IndexAt(Point p)
    {
        var items = Items;
        if (items is null || items.Count == 0)
            return -1;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = size / 2;
        var inner = outer - HoverGrow - Thickness - 4;
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < inner || distance > outer)
            return -1;

        var degrees = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
        var total = items.Sum(i => Math.Max(0, i.Value));
        var acc = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            acc += Math.Max(0, items[i].Value) / total * 360;
            if (degrees <= acc)
                return i;
        }

        return -1;
    }

    private void SetHover(int index)
    {
        if (index == _hover)
            return;

        _hover = index;
        HoveredSlice = index >= 0 && Items is { } items && index < items.Count ? items[index] : null;
        Cursor = index >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        Animate();
    }

    private static StreamGeometry Segment(Point c, double outer, double inner, double startDegrees, double sweepDegrees)
    {
        sweepDegrees = Math.Min(sweepDegrees, 359.99);
        var start = startDegrees * Math.PI / 180;
        var end = (startDegrees + sweepDegrees) * Math.PI / 180;
        var large = sweepDegrees > 180;

        Point At(double radius, double angle) => new(c.X + radius * Math.Cos(angle), c.Y + radius * Math.Sin(angle));

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(At(outer, start), true);
        ctx.ArcTo(At(outer, end), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
        ctx.LineTo(At(inner, end));
        ctx.ArcTo(At(inner, start), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
        ctx.EndFigure(true);
        return geometry;
    }
}
