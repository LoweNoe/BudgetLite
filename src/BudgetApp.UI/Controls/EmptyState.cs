using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace BudgetApp.UI.Controls;

public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(Command));

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
