using BudgetApp.Core.Analysis;
using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public enum InsightAction { None, Transactions, Budgets, Dashboard }

public sealed record InsightItem(string Id, string Text, string Icon, bool IsWarning, bool IsPositive, InsightAction Action, string? ActionText, string? CategoryId)
{
    public bool HasAction => Action != InsightAction.None;
}

public sealed record InsightGroup(string Title, string Caption, bool IsWarning, bool IsPositive, IReadOnlyList<InsightItem> Items);

public sealed partial class InsightsViewModel : ViewModelBase
{
    private readonly BudgetWorkspace _workspace;
    private readonly Navigator _navigator;
    private YearMonth? _month;

    public InsightsViewModel(BudgetWorkspace workspace, Navigator navigator)
    {
        _workspace = workspace;
        _navigator = navigator;

        workspace.TransactionsChanged += (_, _) => Refresh();
        workspace.CategoriesChanged += (_, _) => Refresh();
        workspace.SettingsChanged += (_, _) => Refresh();
        workspace.BudgetsChanged += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty] public partial bool HasData { get; set; }
    [ObservableProperty] public partial string MonthLabel { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<InsightGroup> Groups { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousMonthCommand), nameof(NextMonthCommand))]
    public partial IReadOnlyList<YearMonth> Months { get; set; } = [];

    [RelayCommand]
    private void GoToImport() => _navigator.GoTo(Page.Import);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) - 1]);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) + 1]);

    private bool CanGoPrevious() => _month is { } m && Months.Count > 0 && m > Months[0];

    private bool CanGoNext() => _month is { } m && Months.Count > 0 && m < Months[^1];

    [RelayCommand]
    private void Open(InsightItem? item)
    {
        switch (item?.Action)
        {
            case InsightAction.Transactions:
                _navigator.ShowTransactions(new TransactionFilter(item.CategoryId, _month));
                break;
            case InsightAction.Budgets:
                _navigator.GoTo(Page.Budgets);
                break;
            case InsightAction.Dashboard:
                _navigator.GoTo(Page.Dashboard);
                break;
        }
    }

    private void ShowMonth(YearMonth month)
    {
        _month = month;
        Refresh();
    }

    private void Refresh()
    {
        var report = BudgetAnalyzer.Analyse(_workspace.Transactions, _workspace.Categories, _workspace.Settings, _month, _workspace.Budgets);
        HasData = report is not null;
        if (report is null)
        {
            _month = null;
            Groups = [];
            Months = [];
            return;
        }

        _month = report.Month;
        Months = report.AvailableMonths;
        PreviousMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();
        MonthLabel = report.Month.ToString();

        var items = report.Insights.Select(ToItem).ToList();
        var attention = items.Where(i => i.IsWarning).ToList();
        var good = items.Where(i => i.IsPositive).ToList();
        var neutral = items.Where(i => !i.IsWarning && !i.IsPositive).ToList();

        var groups = new List<InsightGroup>();
        if (attention.Count > 0)
            groups.Add(new InsightGroup("Needs attention", "Things worth acting on soon.", true, false, attention));
        if (good.Count > 0)
            groups.Add(new InsightGroup("Good news", "What's going well.", false, true, good));
        if (neutral.Count > 0)
            groups.Add(new InsightGroup("Worth knowing", "How your money moved.", false, false, neutral));
        Groups = groups;

        var parts = new List<string>();
        if (attention.Count > 0)
            parts.Add($"{attention.Count} {(attention.Count == 1 ? "needs" : "need")} attention");
        if (good.Count > 0)
            parts.Add($"{good.Count} good {(good.Count == 1 ? "sign" : "signs")}");
        if (neutral.Count > 0)
            parts.Add($"{neutral.Count} worth knowing");
        Summary = parts.Count == 0
            ? "Nothing stands out this month."
            : $"{Display.Count(items.Count, "insight")} for {report.Month}: {string.Join(", ", parts)}.";
    }

    // Each insight links to the screen where you'd act on it.
    private static InsightItem ToItem(Insight insight)
    {
        var (action, text) = insight.Id switch
        {
            var id when id.StartsWith("budget", StringComparison.Ordinal) => (InsightAction.Budgets, "Open budgets"),
            var id when id == "forecast" || id.StartsWith("risk-", StringComparison.Ordinal) => (InsightAction.Dashboard, "See forecast"),
            _ when insight.CategoryId is not null => (InsightAction.Transactions, "See transactions"),
            "biggest" or "total-change" or "non-essential" => (InsightAction.Transactions, "See this month"),
            _ => (InsightAction.None, (string?)null),
        };

        return new InsightItem(
            insight.Id,
            insight.Text,
            insight.Icon,
            insight.Kind == InsightKind.Warning,
            insight.Kind == InsightKind.Positive,
            action,
            text,
            insight.CategoryId);
    }
}
