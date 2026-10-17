using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace BudgetApp.UI.Controls;

public class TipCard : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TipCard, string?>(nameof(Text));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<TipCard, ICommand?>(nameof(DismissCommand));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }
}
