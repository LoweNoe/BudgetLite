using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BudgetApp.UI.ViewModels;
using BudgetApp.UI.Views;

namespace BudgetApp.UI;

// Explicit mapping instead of reflection, so it survives trimming in the single-file build.
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data) => data switch
    {
        ShellViewModel => new ShellView(),
        OnboardingViewModel => new OnboardingView(),
        DashboardViewModel => new DashboardView(),
        InsightsViewModel => new InsightsView(),
        TransactionsViewModel => new TransactionsView(),
        BudgetsViewModel => new BudgetsView(),
        ImportViewModel => new ImportView(),
        CategoriesViewModel => new CategoriesView(),
        SettingsViewModel => new SettingsView(),
        _ => new TextBlock { Text = $"No view for {data?.GetType().Name}" },
    };

    public bool Match(object? data) => data is ViewModelBase;
}
