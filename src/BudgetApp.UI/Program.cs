using Avalonia;
using BudgetApp.Core.Services;
using BudgetApp.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetApp.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // No UI thread or sync context exists yet, so blocking here is safe.
        var custom = DataDirFromArgs(args);
        var paths = custom ?? DataLocation.ResolveAsync().GetAwaiter().GetResult();
        var services = AppServices.Build(paths, customDataDir: custom is not null);

        services.GetRequiredService<BudgetWorkspace>().LoadAsync().GetAwaiter().GetResult();
        App.Services = services;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // --data-dir <path> points the app at a different data folder (handy for testing).
    private static DataPaths? DataDirFromArgs(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? new DataPaths(args[index + 1]) : null;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
