using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace BudgetApp.UI.Controls;

// Page change: the old page fades out quickly while the new one fades in and drifts up a few pixels.
public sealed class FadeSlideTransition : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(240);
    public double Offset { get; set; } = 12;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var tasks = new List<Task>();

        if (from is not null)
        {
            var fadeOut = new Animation
            {
                Duration = TimeSpan.FromTicks(Duration.Ticks / 2),
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    Frame(0, 1d, 0d),
                    Frame(1, 0d, -Offset / 3),
                },
            };
            tasks.Add(fadeOut.RunAsync(from, cancellationToken));
        }

        if (to is not null)
        {
            to.IsVisible = true;
            var fadeIn = new Animation
            {
                Duration = Duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    Frame(0, 0d, Offset),
                    Frame(1, 1d, 0d),
                },
            };
            tasks.Add(fadeIn.RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (from is not null && !cancellationToken.IsCancellationRequested)
            from.IsVisible = false;
    }

    private static KeyFrame Frame(double cue, double opacity, double offset) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Visual.OpacityProperty, opacity),
            new Setter(TranslateTransform.YProperty, offset),
        },
    };
}
