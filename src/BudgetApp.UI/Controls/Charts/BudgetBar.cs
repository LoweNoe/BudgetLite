using Avalonia;
using Avalonia.Media;

namespace BudgetApp.UI.Controls.Charts;

// Progress bar for a budget: solid fill for what's spent, a faint extension to where the month
// is heading, and a tick at the limit once either goes past it.
public class BudgetBar : ChartBase
{
    public static readonly StyledProperty<double> SpentProperty =
        AvaloniaProperty.Register<BudgetBar, double>(nameof(Spent));

    public static readonly StyledProperty<double> ProjectedProperty =
        AvaloniaProperty.Register<BudgetBar, double>(nameof(Projected));

    public static readonly StyledProperty<bool> IsWatchProperty =
        AvaloniaProperty.Register<BudgetBar, bool>(nameof(IsWatch));

    public static readonly StyledProperty<bool> IsOverProperty =
        AvaloniaProperty.Register<BudgetBar, bool>(nameof(IsOver));

    static BudgetBar()
    {
        AffectsRender<BudgetBar>(SpentProperty, ProjectedProperty, IsWatchProperty, IsOverProperty);
        HeightProperty.OverrideDefaultValue<BudgetBar>(10);
    }

    public double Spent
    {
        get => GetValue(SpentProperty);
        set => SetValue(SpentProperty, value);
    }

    public double Projected
    {
        get => GetValue(ProjectedProperty);
        set => SetValue(ProjectedProperty, value);
    }

    public bool IsWatch
    {
        get => GetValue(IsWatchProperty);
        set => SetValue(IsWatchProperty, value);
    }

    public bool IsOver
    {
        get => GetValue(IsOverProperty);
        set => SetValue(IsOverProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var radius = height / 2;
        var scale = Math.Max(1, Math.Max(Spent, Projected)) * (Math.Max(Spent, Projected) > 1 ? 1.06 : 1);
        var brush = IsOver ? Brush("NegativeBrush") : IsWatch ? Brush("WarningBrush") : Brush("AccentBrush");

        var track = new Rect(0, 0, width, height);
        using (context.PushClip(new RoundedRect(track, radius)))
        {
            context.FillRectangle(Brush("BorderBrush"), track);

            var projectedWidth = Math.Min(Projected / scale, 1) * width * Progress;
            if (Projected > Spent)
                context.FillRectangle(WithOpacity(brush, 0.3), new Rect(0, 0, projectedWidth, height));

            var spentWidth = Math.Min(Spent / scale, 1) * width * Progress;
            context.DrawRectangle(brush, null, new Rect(0, 0, spentWidth, height), radius, radius);
        }

        if (scale > 1)
        {
            var x = Math.Round(width / scale) + 0.5;
            context.DrawLine(new Pen(Brush("TextPrimaryBrush"), 2), new Point(x, -2), new Point(x, height + 2));
        }
    }
}
