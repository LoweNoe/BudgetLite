using Avalonia;
using Avalonia.Controls;

namespace BudgetApp.UI.Controls;

// Paints itself with a palette token ("chart-3" -> Chart3Brush) and follows theme changes.
public class ColourDot : Border
{
    public static readonly StyledProperty<string?> TokenProperty =
        AvaloniaProperty.Register<ColourDot, string?>(nameof(Token));

    private IDisposable? _resourceBinding;

    public string? Token
    {
        get => GetValue(TokenProperty);
        set => SetValue(TokenProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ColourDot);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TokenProperty)
        {
            _resourceBinding?.Dispose();
            _resourceBinding = Token is { Length: > 0 } token
                ? this.Bind(BackgroundProperty, this.GetResourceObservable(ToResourceKey(token)))
                : null;
        }
    }

    public static string ToResourceKey(string token)
    {
        var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..])) + "Brush";
    }
}
