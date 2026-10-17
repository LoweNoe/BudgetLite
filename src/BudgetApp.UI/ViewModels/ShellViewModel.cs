using BudgetApp.Core.Models;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BudgetApp.UI.ViewModels;

public sealed record NavItem(Page Page, string Title, string Icon, ViewModelBase ViewModel);

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly ThemeService _theme;

    public ShellViewModel(
        DashboardViewModel dashboard,
        InsightsViewModel insights,
        TransactionsViewModel transactions,
        BudgetsViewModel budgets,
        ImportViewModel import,
        CategoriesViewModel categories,
        SettingsViewModel settings,
        ThemeService theme,
        Navigator navigator)
    {
        _theme = theme;
        NavItems =
        [
            new(Page.Dashboard, "Dashboard", "dashboard", dashboard),
            new(Page.Insights, "Insights", "sparkles", insights),
            new(Page.Transactions, "Transactions", "list", transactions),
            new(Page.Budgets, "Budgets", "target", budgets),
            new(Page.Import, "Import", "upload", import),
            new(Page.Categories, "Categories", "tag", categories),
            new(Page.Settings, "Settings", "settings", settings),
        ];

        navigator.PageRequested += Navigate;
        theme.ThemeChanged += (_, _) => OnPropertyChanged(nameof(IsDarkMode));
        SelectedNav = NavItems[0];
    }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNav { get; set; }

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    public bool IsDarkMode
    {
        get => _theme.IsDark;
        set
        {
            if (value != _theme.IsDark)
                _ = _theme.SetAsync(value ? ThemePreference.Dark : ThemePreference.Light);
        }
    }

    public void Navigate(Page page) => SelectedNav = NavItems.First(n => n.Page == page);

    // Used when the app is opened with statement files (drag onto the exe, or "Open with").
    public async Task ImportFilesAsync(IReadOnlyList<BudgetApp.Core.Import.StatementFile> files)
    {
        Navigate(Page.Import);
        if (NavItems.First(n => n.Page == Page.Import).ViewModel is ImportViewModel import)
            await import.LoadFilesAsync(files);
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
            return;

        if (value.ViewModel is ImportViewModel { IsFinished: true } import)
            import.Reset();

        CurrentPage = value.ViewModel;
    }
}
