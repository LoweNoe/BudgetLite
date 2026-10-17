using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace BudgetApp.UI.Controls.Charts;

public sealed record ChartSlice(string Label, double Value, string ColourToken, string ValueText, object? Tag = null);

public sealed record ChartBar(string Label, double Value, double HighlightValue, string ValueText, bool IsSelected, object? Tag = null);

public sealed record ChartPoint(string Label, double Value, string ValueText, string? Marker = null, bool IsWarning = false, double? Low = null, double? High = null);

// Shared plumbing: an entrance animation (Progress 0 -> 1), a frame loop for hover easing,
// and brushes looked up from the theme at render time so charts repaint when the theme flips.
public abstract class ChartBase : Control, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ChartBase, double>(nameof(Progress), 1);

    private Transitions? _transitions;
    private bool _frameRequested;
    private TimeSpan? _lastFrame;

    static ChartBase()
    {
        AffectsRender<ChartBase>(ProgressProperty);
    }

    protected ChartBase()
    {
        _transitions =
        [
            new DoubleTransition { Property = ProgressProperty, Duration = TimeSpan.FromMilliseconds(800), Easing = new QuinticEaseOut() },
        ];
        Transitions = _transitions;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    // The whole rectangle reacts to the pointer, including empty space between bars or inside the donut.
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Replay();
    }

    protected void Replay()
    {
        Transitions = null;
        Progress = 0;
        Transitions = _transitions;
        Dispatcher.UIThread.Post(() => Progress = 1, DispatcherPriority.Render);
    }

    // Override to advance hover easing; return true while anything is still moving.
    protected virtual bool Step(double seconds) => false;

    protected void Animate()
    {
        if (_frameRequested)
            return;

        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            InvalidateVisual();
            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        _frameRequested = false;
        var seconds = _lastFrame is { } last ? Math.Clamp((time - last).TotalSeconds, 0, 0.05) : 1 / 60.0;
        _lastFrame = time;

        var moving = Step(seconds);
        InvalidateVisual();
        if (moving)
            Animate();
        else
            _lastFrame = null;
    }

    protected static double Approach(double current, double target, double seconds, double speed = 16) =>
        Math.Abs(target - current) < 0.002 ? target : current + (target - current) * Math.Min(1, seconds * speed);

    protected IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    protected IBrush TokenBrush(string token) => Brush(ColourDot.ToResourceKey(token));

    protected static IBrush WithOpacity(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;

    protected double Resource(string key, double fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is double d ? d : fallback;

    protected FormattedText Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
    {
        var family = GetValue(TextElement.FontFamilyProperty);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyle.Normal, weight), size, brush);
    }

    protected void DrawTooltip(DrawingContext context, Point anchor, string title, string value)
    {
        var titleText = Text(title, Resource("FontSizeCaption", 12), Brush("TextSecondaryBrush"));
        var valueText = Text(value, Resource("FontSizeBody", 14), Brush("TextPrimaryBrush"), FontWeight.SemiBold);
        var width = Math.Max(titleText.Width, valueText.Width) + 20;
        var height = titleText.Height + valueText.Height + 14;

        var x = Math.Clamp(anchor.X - width / 2, 0, Math.Max(0, Bounds.Width - width));
        var y = anchor.Y - height - 10;
        if (y < 0)
            y = anchor.Y + 12;

        var rect = new Rect(x, y, width, height);
        context.DrawRectangle(Brush("ElevatedBrush"), new Pen(Brush("BorderBrush")), rect, 6, 6,
            new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 14, Color = Color.FromArgb(40, 0, 0, 0) }));
        context.DrawText(titleText, new Point(x + 10, y + 6));
        context.DrawText(valueText, new Point(x + 10, y + 8 + titleText.Height));
    }
}
