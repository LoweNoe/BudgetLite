using BudgetApp.Core.Services;

namespace BudgetApp.UI.Services;

public static class TipKeys
{
    public const string CategoryEdit = "transactions.category-edit";
    public const string DashboardTrend = "dashboard.trend";
    public const string CategoriesEssential = "categories.essential";
    public const string Budgets = "budgets.intro";
}

public sealed class TipsService
{
    private readonly BudgetWorkspace _workspace;

    public TipsService(BudgetWorkspace workspace)
    {
        _workspace = workspace;
    }

    public event EventHandler? TipsReset;

    public bool IsDismissed(string key) => _workspace.Settings.DismissedTips.Contains(key);

    public Task DismissAsync(string key) =>
        _workspace.Settings.DismissedTips.Add(key) ? _workspace.SaveSettingsAsync() : Task.CompletedTask;

    public async Task ResetAsync()
    {
        _workspace.Settings.DismissedTips.Clear();
        await _workspace.SaveSettingsAsync();
        TipsReset?.Invoke(this, EventArgs.Empty);
    }
}
