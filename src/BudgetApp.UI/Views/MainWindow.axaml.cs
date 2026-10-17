using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BudgetApp.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetApp.UI.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan CrossFade = TimeSpan.FromMilliseconds(380);
    private RenderTargetBitmap? _snapshot;

    public MainWindow()
    {
        InitializeComponent();

        if (App.Services?.GetService<ThemeService>() is { } theme)
            theme.ThemeChanging += (_, _) => CaptureForCrossFade();
    }

    // Freeze a picture of the window in the old theme, lay it on top, switch theme underneath,
    // then fade the picture away. Every colour appears to blend at once, charts included.
    private void CaptureForCrossFade()
    {
        var size = RootContent.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
            return;

        var scale = RenderScaling;
        var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(RootContent);

        _snapshot?.Dispose();
        _snapshot = bitmap;

        ThemeSnapshot.Transitions = null;
        ThemeSnapshot.Source = bitmap;
        ThemeSnapshot.Opacity = 1;
        ThemeSnapshot.IsVisible = true;

        Dispatcher.UIThread.Post(() =>
        {
            ThemeSnapshot.Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = CrossFade, Easing = new CubicEaseInOut() },
            ];
            ThemeSnapshot.Opacity = 0;
        }, DispatcherPriority.Background);

        DispatcherTimer.RunOnce(() =>
        {
            if (_snapshot != bitmap)
                return;

            ThemeSnapshot.IsVisible = false;
            ThemeSnapshot.Source = null;
            _snapshot = null;
            bitmap.Dispose();
        }, CrossFade + TimeSpan.FromMilliseconds(80));
    }
}
