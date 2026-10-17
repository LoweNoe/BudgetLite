using Avalonia;
using Avalonia.Controls;

namespace BudgetApp.UI.Controls;

// Lays children side by side (sized by their Weight) when there's room, otherwise stacks them.
public class ResponsiveRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveRow, double>(nameof(Spacing), 16);

    public static readonly StyledProperty<double> BreakWidthProperty =
        AvaloniaProperty.Register<ResponsiveRow, double>(nameof(BreakWidth), 760);

    public static readonly AttachedProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<ResponsiveRow, Control, double>("Weight", 1);

    static ResponsiveRow()
    {
        AffectsMeasure<ResponsiveRow>(SpacingProperty, BreakWidthProperty);
        AffectsParentMeasure<ResponsiveRow>(WeightProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double BreakWidth
    {
        get => GetValue(BreakWidthProperty);
        set => SetValue(BreakWidthProperty, value);
    }

    public static double GetWeight(Control control) => control.GetValue(WeightProperty);
    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    private List<Control> Visible => Children.Where(c => c.IsVisible).ToList();

    private bool IsHorizontal(double width, int count) => count > 1 && width >= BreakWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Visible;
        if (children.Count == 0)
            return default;

        var width = double.IsInfinity(availableSize.Width) ? BreakWidth : availableSize.Width;
        if (IsHorizontal(width, children.Count))
        {
            var widths = ColumnWidths(children, width);
            var height = 0.0;
            for (var i = 0; i < children.Count; i++)
            {
                children[i].Measure(new Size(widths[i], double.PositiveInfinity));
                height = Math.Max(height, children[i].DesiredSize.Height);
            }

            return new Size(width, height);
        }

        var total = 0.0;
        foreach (var child in children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            total += child.DesiredSize.Height;
        }

        return new Size(width, total + Spacing * (children.Count - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Visible;
        if (IsHorizontal(finalSize.Width, children.Count))
        {
            var widths = ColumnWidths(children, finalSize.Width);
            var x = 0.0;
            for (var i = 0; i < children.Count; i++)
            {
                children[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
                x += widths[i] + Spacing;
            }
        }
        else
        {
            var y = 0.0;
            foreach (var child in children)
            {
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height + Spacing;
            }
        }

        return finalSize;
    }

    private double[] ColumnWidths(List<Control> children, double width)
    {
        var usable = Math.Max(0, width - Spacing * (children.Count - 1));
        var weights = children.Select(c => Math.Max(0.01, GetWeight(c))).ToArray();
        var sum = weights.Sum();
        return weights.Select(w => usable * w / sum).ToArray();
    }
}
