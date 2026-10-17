using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace BudgetApp.UI.Controls;

public class IconView : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconView, Geometry?>(nameof(Data));

    // Name of an icon in Icons.axaml without the "Icon." prefix, e.g. "cart".
    public static readonly StyledProperty<string?> KeyProperty =
        AvaloniaProperty.Register<IconView, string?>(nameof(Key));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<IconView, double>(nameof(StrokeThickness), 2);

    private IDisposable? _resourceBinding;

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KeyProperty)
        {
            _resourceBinding?.Dispose();
            _resourceBinding = Key is { Length: > 0 } key
                ? this.Bind(DataProperty, this.GetResourceObservable($"Icon.{key}", r => r as Geometry))
                : null;
        }
    }
}
