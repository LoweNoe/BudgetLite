using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using BudgetApp.Core.Formatting;

namespace BudgetApp.UI.Controls;

// A money TextBlock that counts up to its value, and glides between values when it changes.
public class AnimatedAmount : TextBlock
{
    public static readonly StyledProperty<decimal> ValueProperty =
        AvaloniaProperty.Register<AnimatedAmount, decimal>(nameof(Value));

    public static readonly StyledProperty<string> CurrencyProperty =
        AvaloniaProperty.Register<AnimatedAmount, string>(nameof(Currency), "ZAR");

    public static readonly StyledProperty<bool> WholeUnitsProperty =
        AvaloniaProperty.Register<AnimatedAmount, bool>(nameof(WholeUnits));

    private static readonly StyledProperty<double> DisplayProperty =
        AvaloniaProperty.Register<AnimatedAmount, double>("Display");

    private readonly Transitions _transitions =
    [
        new DoubleTransition { Property = DisplayProperty, Duration = TimeSpan.FromMilliseconds(900), Easing = new QuarticEaseOut() },
    ];

    public AnimatedAmount()
    {
        Transitions = _transitions;
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public decimal Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Currency
    {
        get => GetValue(CurrencyProperty);
        set => SetValue(CurrencyProperty, value);
    }

    public bool WholeUnits
    {
        get => GetValue(WholeUnitsProperty);
        set => SetValue(WholeUnitsProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Transitions = null;
        SetValue(DisplayProperty, 0d);
        UpdateText();
        Transitions = _transitions;
        SetValue(DisplayProperty, (double)Value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
            SetValue(DisplayProperty, (double)Value);
        else if (change.Property == DisplayProperty || change.Property == CurrencyProperty || change.Property == WholeUnitsProperty)
            UpdateText();
    }

    // Snap to the exact value at the end so rounding in the double never shows.
    private void UpdateText()
    {
        var display = GetValue(DisplayProperty);
        var amount = Math.Abs(display - (double)Value) < 0.005 ? Value : (decimal)display;
        Text = MoneyFormatter.Format(amount, Currency, WholeUnits);
    }
}
