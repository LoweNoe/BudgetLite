using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Services;
using BudgetApp.Data;
using BudgetApp.Import;
using BudgetApp.Import.Csv;
using BudgetApp.UI.Services;
using BudgetApp.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetApp.UI;

public static class AppServices
{
    public static ServiceProvider Build(DataPaths paths, bool customDataDir = false)
    {
        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddSingleton(new AppLaunchOptions(customDataDir));
        services.AddSingleton<IDataStore, JsonDataStore>();
        services.AddSingleton<IBankTipsProvider, BankTipsProvider>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<RuleBasedCategoriser>();
        services.AddSingleton<ICategoriser>(sp => sp.GetRequiredService<RuleBasedCategoriser>());
        services.AddSingleton<BudgetWorkspace>();

        services.AddSingleton<IStatementParser, CsvStatementParser>();
        services.AddSingleton<IStatementParser, BudgetApp.Import.Pdf.PdfStatementParser>();
        services.AddSingleton<StatementImporter>();

        services.AddSingleton<ThemeService>();
        services.AddSingleton<TipsService>();
        services.AddSingleton<Navigator>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<IPlatformDialogs, PlatformDialogs>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<InsightsViewModel>();
        services.AddSingleton<TransactionsViewModel>();
        services.AddSingleton<BudgetsViewModel>();
        services.AddSingleton<CategoriesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<OnboardingViewModel>();

        return services.BuildServiceProvider();
    }
}

// True when launched with --data-dir, in which case moving the data folder is disabled.
public sealed record AppLaunchOptions(bool CustomDataDir);
