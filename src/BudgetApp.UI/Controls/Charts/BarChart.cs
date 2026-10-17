using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using BudgetApp.Core.Formatting;

namespace BudgetApp.UI.Controls.Charts;

// Stacked bars: the full bar is total spending, the coral cap is the non-essential part.
public class BarChart : ChartBase
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> ItemsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Items));

    public static readonly StyledProperty<ICommand?> BarCommandProperty =
        AvaloniaProperty.Register<BarChart, ICommand?>(nameof(BarCommand));

    public static readonly StyledProperty<string> CurrencyProperty =
        AvaloniaProperty.Register<BarChart, string>(nameof(Currency), "ZAR");

    private const double AxisWidth = 52;
    private const double LabelHeight = 24;
    private const double MaxBarWidth = 34;

    private double[] _hoverAmount = [];
    private int _hover = -1;

    public IReadOnlyList<ChartBar>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ICommand? BarCommand
    {
        get => GetValue(BarCommandProperty);
        set => SetValue(BarCommandProperty, value);
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
            var old = change.OldValue as IReadOnlyList<ChartBar>;
            var fresh = change.NewValue as IReadOnlyList<ChartBar>;
            _hoverAmount = new double[fresh?.Count ?? 0];

            // Only replay the grow-in when the months shown change, not when the selection moves.
            var sameMonths = old is not null && fresh is not null && old.Select(b => b.Label).SequenceEqual(fresh.Select(b => b.Label))
                             && old.Select(b => b.Value).SequenceEqual(fresh.Select(b => b.Value));
            if (sameMonths)
                InvalidateVisual();
            else if (VisualRoot is not null)
                Replay();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var index = IndexAt(e.GetPosition(this));
        if (index == _hover)
            return;

        _hover = index;
        Cursor = index >= 0 && BarCommand is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        Animate();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        Animate();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var index = IndexAt(e.GetPosition(this));
        if (index >= 0 && Items is { } items && BarCommand is { } command && command.CanExecute(items[index].Tag))
            command.Execute(items[index].Tag);
    }

    protected override bool Step(double seconds)
    {
        var moving = false;
        for (var i = 0; i < _hoverAmount.Length; i++)
        {
            var target = i == _hover ? 1 : 0;
            _hoverAmount[i] = Approach(_hoverAmount[i], target, seconds, 14);
            moving |= _hoverAmount[i] != target;
        }

        return moving;
    }

    public override void Render(DrawingContext context)
    {
        var items = Items ?? [];
        var plot = PlotArea();
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var max = NiceMax(items.Count == 0 ? 0 : items.Max(i => i.Value));
        var grid = new Pen(Brush("BorderBrush"), 1, new DashStyle([4, 4], 0));
        var captionSize = Resource("FontSizeCaption", 12);
        var secondary = Brush("TextSecondaryBrush");

        for (var step = 0; step <= 2; step++)
        {
            var value = max * step / 2;
            var y = plot.Bottom - plot.Height * step / 2;
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(MoneyFormatter.FormatCompact((decimal)value, Currency), captionSize, secondary);
            context.DrawText(label, new Point(plot.Left - label.Width - 10, y - label.Height / 2));
        }

        if (items.Count == 0)
            return;

        var slot = plot.Width / items.Count;
        var barWidth = Math.Min(MaxBarWidth, slot * 0.55);
        var accent = Brush("AccentBrush");
        var highlight = Brush("NegativeBrush");
        var anySelected = items.Any(i => i.IsSelected);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var stagger = Math.Clamp(Progress * 1.35 - i * 0.07, 0, 1);
            var height = max <= 0 ? 0 : item.Value / max * plot.Height * stagger;
            var centerX = plot.Left + slot * (i + 0.5);
            var hover = i < _hoverAmount.Length ? _hoverAmount[i] : 0;
            var width = barWidth + hover * 4;
            var bar = new Rect(centerX - width / 2, plot.Bottom - height, width, height);

            var dim = anySelected && !item.IsSelected ? 0.6 + hover * 0.4 : 1;
            if (height > 0.5)
            {
                using (context.PushOpacity(dim))
                using (context.PushClip(new RoundedRect(bar, new CornerRadius(6, 6, 2, 2))))
                {
                    context.FillRectangle(accent, bar);
                    var capHeight = item.Value <= 0 ? 0 : height * Math.Clamp(item.HighlightValue / item.Value, 0, 1);
                    context.FillRectangle(highlight, new Rect(bar.X, bar.Y, bar.Width, capHeight));
                }
            }

            var label = Text(item.Label, captionSize, item.IsSelected ? Brush("TextPrimaryBrush") : secondary,
                item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal);
            context.DrawText(label, new Point(centerX - label.Width / 2, plot.Bottom + 6));
        }

        if (_hover >= 0 && _hover < items.Count && Progress > 0.9)
        {
            var item = items[_hover];
            var x = plot.Left + slot * (_hover + 0.5);
            var y = plot.Bottom - (max <= 0 ? 0 : item.Value / max * plot.Height);
            DrawTooltip(context, new Point(x, y), item.Label, item.ValueText);
        }
    }

    private Rect PlotArea() => new(AxisWidth, 8, Math.Max(0, Bounds.Width - AxisWidth - 4), Math.Max(0, Bounds.Height - LabelHeight - 8));

    private int IndexAt(Point p)
    {
        var items = Items;
        var plot = PlotArea();
        if (items is null || items.Count == 0 || p.X < plot.Left || p.X > plot.Right || p.Y < 0 || p.Y > Bounds.Height)
            return -1;
        return Math.Clamp((int)((p.X - plot.Left) / (plot.Width / items.Count)), 0, items.Count - 1);
    }

    // Rounds the axis maximum up to 1, 2, 2.5 or 5 times a power of ten so gridlines land on tidy values.
    public static double NiceMax(double value)
    {
        if (value <= 0)
            return 1000;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (step * magnitude >= value * 1.05)
                return step * magnitude;
        }

        return 10 * magnitude;
    }
}
