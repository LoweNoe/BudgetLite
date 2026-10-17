using System.Globalization;
using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed partial class BudgetRowViewModel : ObservableObject
{
    private readonly Func<BudgetRowViewModel, decimal, Task> _save;
    private readonly Func<BudgetRowViewModel, Task> _remove;

    public BudgetRowViewModel(BudgetStatus status, string currency, bool inProgress, int daysLeft,
        Func<BudgetRowViewModel, decimal, Task> save, Func<BudgetRowViewModel, Task> remove)
    {
        Status = status;
        _save = save;
        _remove = remove;

        AmountText = $"{MoneyFormatter.Format(status.Spent, currency, wholeUnits: true)} of {MoneyFormatter.Format(status.Limit, currency, wholeUnits: true)}";
        DetailText = status.State switch
        {
            BudgetState.Over => $"{MoneyFormatter.Format(status.Spent - status.Limit, currency, wholeUnits: true)} over",
            _ when inProgress => $"{MoneyFormatter.Format(status.Remaining, currency, wholeUnits: true)} left · {Display.Count(daysLeft, "day")} to go",
            _ => $"{MoneyFormatter.Format(status.Remaining, currency, wholeUnits: true)} to spare",
        };
        PaceText = inProgress && status.Projected > status.Spent
            ? $"On pace for {MoneyFormatter.Format(status.Projected, currency, wholeUnits: true)}"
            : "";
        StateText = status.State switch
        {
            BudgetState.Over => "Over",
            BudgetState.Watch => "Watch",
            _ => "On track",
        };
        EditAmount = status.Limit;
    }

    public BudgetStatus Status { get; }
    public string CategoryId => Status.CategoryId;
    public string Name => Status.Name;
    public string Icon => Status.Icon;
    public string Colour => Status.Colour;
    public double UsedFraction => Status.UsedFraction;
    public double ProjectedFraction => Status.ProjectedFraction;
    public bool IsOver => Status.State == BudgetState.Over;
    public bool IsWatch => Status.State == BudgetState.Watch;
    public bool IsOnTrack => Status.State == BudgetState.OnTrack;
    public string AmountText { get; }
    public string DetailText { get; }
    public string PaceText { get; }
    public bool HasPace => PaceText.Length > 0;
    public string StateText { get; }

    [ObservableProperty] public partial bool IsEditing { get; set; }
    [ObservableProperty] public partial decimal? EditAmount { get; set; }

    [RelayCommand]
    private void Edit() => IsEditing = true;

    [RelayCommand]
    private void CancelEdit()
    {
        EditAmount = Status.Limit;
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (EditAmount is not { } amount || amount <= 0)
            return;
        IsEditing = false;
        await _save(this, amount);
    }

    [RelayCommand]
    private Task Remove() => _remove(this);
}

public sealed partial class SuggestionRow : ObservableObject
{
    public SuggestionRow(BudgetSuggestion suggestion, string currency)
    {
        Suggestion = suggestion;
        Amount = suggestion.Amount;
        AverageText = $"You average {MoneyFormatter.Format(suggestion.MonthlyAverage, currency, wholeUnits: true)} a month";
    }

    public BudgetSuggestion Suggestion { get; }
    public string Name => Suggestion.Name;
    public string Icon => Suggestion.Icon;
    public string Colour => Suggestion.Colour;
    public string AverageText { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
    [ObservableProperty] public partial decimal? Amount { get; set; }
}

public sealed partial class BudgetsViewModel : ViewModelBase
{
    private readonly BudgetWorkspace _workspace;
    private readonly Navigator _navigator;
    private readonly ToastService _toasts;
    private readonly TipsService _tips;
    private YearMonth? _month;
    private IReadOnlyList<BudgetSuggestion> _suggestions = [];

    public BudgetsViewModel(BudgetWorkspace workspace, Navigator navigator, ToastService toasts, TipsService tips)
    {
        _workspace = workspace;
        _navigator = navigator;
        _toasts = toasts;
        _tips = tips;

        workspace.TransactionsChanged += (_, _) => Refresh();
        workspace.BudgetsChanged += (_, _) => Refresh();
        workspace.CategoriesChanged += (_, _) => Refresh();
        workspace.SettingsChanged += (_, _) => Refresh();
        tips.TipsReset += (_, _) => ShowTip = !_tips.IsDismissed(TipKeys.Budgets);
        ShowTip = !_tips.IsDismissed(TipKeys.Budgets);
        Refresh();
    }

    public string Currency => _workspace.Settings.Currency;

    [ObservableProperty] public partial bool HasData { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasBudgets { get; set; }

    public bool ShowEmptyState => !HasBudgets && !IsSuggesting && !IsAdding;
    [ObservableProperty] public partial bool ShowTip { get; set; }
    [ObservableProperty] public partial string MonthLabel { get; set; } = "";
    [ObservableProperty] public partial string Caption { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousMonthCommand), nameof(NextMonthCommand))]
    public partial IReadOnlyList<YearMonth> Months { get; set; } = [];

    [ObservableProperty] public partial IReadOnlyList<BudgetRowViewModel> Rows { get; set; } = [];

    // Totals
    [ObservableProperty] public partial decimal TotalLimit { get; set; }
    [ObservableProperty] public partial decimal TotalSpent { get; set; }
    [ObservableProperty] public partial decimal TotalLeft { get; set; }
    [ObservableProperty] public partial double TotalUsed { get; set; }
    [ObservableProperty] public partial double TotalProjected { get; set; }
    [ObservableProperty] public partial bool TotalIsOver { get; set; }
    [ObservableProperty] public partial bool TotalIsWatch { get; set; }
    [ObservableProperty] public partial string TotalSentence { get; set; } = "";
    [ObservableProperty] public partial string LeftLabel { get; set; } = "Left";

    // Add form
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsAdding { get; set; }
    [ObservableProperty] public partial IReadOnlyList<CategoryOption> AvailableCategories { get; set; } = [];
    [ObservableProperty] public partial CategoryOption? AddCategory { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAddCommand))]
    public partial decimal? AddAmount { get; set; }

    [ObservableProperty] public partial string AddHint { get; set; } = "";

    // Suggestions
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsSuggesting { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SuggestionRow> Suggestions { get; set; } = [];
    [ObservableProperty] public partial string SuggestionsCaption { get; set; } = "";

    public bool CanAddMore => AvailableCategories.Count > 0;

    partial void OnAddCategoryChanged(CategoryOption? value)
    {
        var suggestion = value is null ? null : _suggestions.FirstOrDefault(s => s.CategoryId == value.Id);
        AddAmount = suggestion?.Amount ?? AddAmount;
        AddHint = suggestion is null
            ? "No recent spending here, so pick an amount that feels right."
            : $"You usually spend about {MoneyFormatter.Format(suggestion.MonthlyAverage, Currency, wholeUnits: true)} a month here.";
        SaveAddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void GoToImport() => _navigator.GoTo(Page.Import);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) - 1]);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) + 1]);

    private bool CanGoPrevious() => _month is { } m && Months.Count > 0 && m > Months[0];

    private bool CanGoNext() => _month is { } m && Months.Count > 0 && m < Months[^1];

    [RelayCommand]
    private void StartAdd()
    {
        IsSuggesting = false;
        IsAdding = true;
        AddAmount = null;
        AddCategory = AvailableCategories.FirstOrDefault();
    }

    [RelayCommand]
    private void CancelAdd() => IsAdding = false;

    [RelayCommand(CanExecute = nameof(CanSaveAdd))]
    private async Task SaveAdd()
    {
        if (AddCategory is not { } category || AddAmount is not { } amount)
            return;

        IsAdding = false;
        await _workspace.SetBudgetAsync(category.Id, amount);
        _toasts.Show($"{category.Name} budget set to {MoneyFormatter.Format(amount, Currency, wholeUnits: true)} a month.");
    }

    private bool CanSaveAdd() => AddCategory is not null && AddAmount > 0;

    [RelayCommand]
    private void ShowSuggestions()
    {
        IsAdding = false;
        if (_suggestions.Count == 0)
        {
            _toasts.Show(_workspace.Transactions.Count == 0
                ? "Import a statement first so we can suggest budgets from your spending."
                : "Every category you spend in already has a budget.", ToastKind.Info);
            return;
        }

        Suggestions = _suggestions.Select(s => new SuggestionRow(s, Currency)).ToList();
        var months = _suggestions[0].Months;
        SuggestionsCaption = $"Based on your average spending over the last {(months == 1 ? "month" : $"{months} months")}, rounded up a little. Untick any you don't want.";
        IsSuggesting = true;
    }

    [RelayCommand]
    private void CancelSuggestions() => IsSuggesting = false;

    [RelayCommand]
    private async Task ApplySuggestions()
    {
        var chosen = Suggestions
            .Where(s => s.IsSelected && s.Amount > 0)
            .Select(s => new CategoryBudget { CategoryId = s.Suggestion.CategoryId, Amount = s.Amount!.Value })
            .ToList();

        IsSuggesting = false;
        if (chosen.Count == 0)
            return;

        var added = await _workspace.AddBudgetsAsync(chosen);
        _toasts.Show($"Added {Display.Count(added, "budget")}. We'll let you know if you're heading over.");
    }

    [RelayCommand]
    private async Task DismissTip()
    {
        ShowTip = false;
        await _tips.DismissAsync(TipKeys.Budgets);
    }

    private async Task SaveRow(BudgetRowViewModel row, decimal amount)
    {
        await _workspace.SetBudgetAsync(row.CategoryId, amount);
        _toasts.Show($"{row.Name} budget updated to {MoneyFormatter.Format(amount, Currency, wholeUnits: true)}.");
    }

    private async Task RemoveRow(BudgetRowViewModel row)
    {
        var removed = await _workspace.RemoveBudgetAsync(row.CategoryId);
        if (removed is null)
            return;

        _toasts.Show($"Removed the {row.Name} budget.", ToastKind.Info, "Undo",
            () => _workspace.SetBudgetAsync(removed.CategoryId, removed.Amount));
    }

    private void ShowMonth(YearMonth month)
    {
        _month = month;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Currency));
        var transactions = _workspace.Transactions;
        HasData = transactions.Count > 0;
        HasBudgets = _workspace.Budgets.Count > 0;

        _suggestions = BudgetTracker.Suggest(transactions, _workspace.Categories, _workspace.Budgets);
        AvailableCategories = _workspace.Categories
            .Where(c => _workspace.FindBudget(c.Id) is null && c.Id is not (CategoryIds.Income or CategoryIds.Transfers))
            .Select(CategoryOption.From)
            .ToList();
        OnPropertyChanged(nameof(CanAddMore));

        var report = BudgetAnalyzer.Analyse(transactions, _workspace.Categories, _workspace.Settings, _month, _workspace.Budgets);
        if (report is null)
        {
            _month = null;
            Months = [];
            Rows = [];
            MonthLabel = DateTime.Today.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            Caption = "Budgets follow your imported months.";
            ClearTotals();
            return;
        }

        _month = report.Month;
        Months = report.AvailableMonths;
        PreviousMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();
        MonthLabel = report.Month.ToString();

        if (report.Budgets is not { } budgets)
        {
            Rows = [];
            ClearTotals();
            Caption = report.ComparedThroughDay is { } d ? $"1–{d} {report.Month.First:MMMM} so far" : $"{report.Month}";
            return;
        }

        Caption = budgets.InProgress
            ? $"{Display.Count(budgets.DaysLeft, "day")} left in {report.Month.First:MMMM}"
            : $"{report.Month} is complete";

        Rows = budgets.Items
            .Select(s => new BudgetRowViewModel(s, Currency, budgets.InProgress, budgets.DaysLeft, SaveRow, RemoveRow))
            .ToList();

        TotalLimit = budgets.TotalLimit;
        TotalSpent = budgets.TotalSpent;
        TotalLeft = Math.Abs(budgets.TotalLimit - budgets.TotalSpent);
        LeftLabel = budgets.TotalSpent > budgets.TotalLimit ? "Over" : "Left";
        TotalUsed = budgets.TotalLimit > 0 ? (double)(budgets.TotalSpent / budgets.TotalLimit) : 0;
        TotalProjected = budgets.TotalLimit > 0 ? (double)(budgets.TotalProjected / budgets.TotalLimit) : 0;
        TotalIsOver = budgets.TotalSpent > budgets.TotalLimit;
        TotalIsWatch = !TotalIsOver && budgets.TotalProjected > budgets.TotalLimit;

        TotalSentence = (budgets.OverCount, budgets.WatchCount) switch
        {
            (0, 0) when budgets.InProgress => $"Everything's on track. You're on pace to spend {MoneyFormatter.Format(budgets.TotalProjected, Currency, wholeUnits: true)} of {MoneyFormatter.Format(budgets.TotalLimit, Currency, wholeUnits: true)}.",
            (0, 0) => "You stayed within every budget this month.",
            (var over, 0) => $"{Display.Count(over, "budget")} {(over == 1 ? "is" : "are")} over. The rest are fine.",
            (0, var watch) => $"{Display.Count(watch, "budget")} could go over if spending carries on like this.",
            var (over, watch) => $"{Display.Count(over, "budget")} over and {watch} to watch.",
        };
    }

    private void ClearTotals()
    {
        TotalLimit = TotalSpent = TotalLeft = 0;
        TotalUsed = TotalProjected = 0;
        TotalIsOver = TotalIsWatch = false;
        TotalSentence = "";
    }
}
