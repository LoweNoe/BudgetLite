using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BudgetApp.UI.Services;
using BudgetApp.UI.ViewModels;
using BudgetApp.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetApp.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            // Settings are already loaded, so the saved theme is in place before the window is created.
            Services.GetRequiredService<ThemeService>().ApplyCurrent();
            var main = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = main };

            var files = StatementArgs(desktop.Args ?? []);
            if (files.Count > 0)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    var statements = await ReadAllAsync(files);
                    if (main.Content is ShellViewModel shell)
                        await shell.ImportFilesAsync(statements);
                    else if (main.Content is OnboardingViewModel onboarding)
                        await onboarding.ImportFilesAsync(statements);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static List<string> StatementArgs(string[] args) =>
        args.Where(a => File.Exists(a) && Path.GetExtension(a).ToLowerInvariant() is ".csv" or ".txt" or ".pdf").ToList();

    private static async Task<IReadOnlyList<BudgetApp.Core.Import.StatementFile>> ReadAllAsync(List<string> paths)
    {
        var files = new List<BudgetApp.Core.Import.StatementFile>();
        foreach (var path in paths)
            files.Add(new BudgetApp.Core.Import.StatementFile(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
        return files;
    }
}
