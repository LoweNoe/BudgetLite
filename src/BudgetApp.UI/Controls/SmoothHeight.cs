using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace BudgetApp.UI.Controls;

// Sizes itself to its content like a normal container, but glides to each new height instead of
// jumping, so a card can grow or shrink smoothly when what's inside it changes.
public class SmoothHeight : Decorator
{
    private static readonly StyledProperty<double> AnimatedHeightProperty =
        AvaloniaProperty.Register<SmoothHeight, double>("AnimatedHeight", double.NaN);

    private readonly Transitions _transitions =
    [
        new DoubleTransition { Property = AnimatedHeightProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseOut() },
    ];

    private double _target = double.NaN;

    static SmoothHeight()
    {
        AffectsMeasure<SmoothHeight>(AnimatedHeightProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SmoothHeight>(true);
    }

    public SmoothHeight()
    {
        Transitions = _transitions;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
            return default;

        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = Child.DesiredSize;
        var target = Math.Max(desired.Height, MinHeight);

        if (!target.Equals(_target))
        {
            var first = double.IsNaN(_target) || VisualRoot is null;
            _target = target;

            // The first size is taken straight away; only later changes animate.
            if (first)
            {
                Transitions = null;
                SetValue(AnimatedHeightProperty, target);
                Transitions = _transitions;
            }
            else
            {
                SetValue(AnimatedHeightProperty, target);
            }
        }

        var height = GetValue(AnimatedHeightProperty);
        return new Size(desired.Width, double.IsNaN(height) ? target : height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The child always gets its full height; anything beyond the animated height is clipped.
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(finalSize.Height, Child.DesiredSize.Height)));
        return finalSize;
    }
}
