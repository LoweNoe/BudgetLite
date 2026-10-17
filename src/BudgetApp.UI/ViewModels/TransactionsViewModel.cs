using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed record CorrectionPrompt(CategoryRule Rule, string Message);

public sealed record MonthOption(string Label, YearMonth? Month);

public sealed partial class TransactionsViewModel : ViewModelBase
{
    private static readonly CategoryOption AllCategories = new("", "All categories", "tag", "chart-8");
    private static readonly MonthOption AllTime = new("All time", null);

    private readonly BudgetWorkspace _workspace;
    private readonly Navigator _navigator;
    private readonly TipsService _tips;
    private readonly ToastService _toasts;
    private List<TransactionRowViewModel> _allRows = [];
    private bool _updating;
    private bool _batchFilter;

    public TransactionsViewModel(BudgetWorkspace workspace, Navigator navigator, TipsService tips, ToastService toasts)
    {
        _workspace = workspace;
        _navigator = navigator;
        _tips = tips;
        _toasts = toasts;

        workspace.TransactionsChanged += (_, _) => { if (!_updating) Rebuild(); };
        workspace.CategoriesChanged += (_, _) => Rebuild();
        workspace.SettingsChanged += (_, _) => Rebuild();
        navigator.TransactionFilterRequested += ApplyExternalFilter;
        tips.TipsReset += (_, _) => ShowCategoryTip = HasTransactions && !_tips.IsDismissed(TipKeys.CategoryEdit);

        SelectedDirection = Directions[0];
        Rebuild();
    }

    public IReadOnlyList<string> Directions { get; } = ["Money in and out", "Money out", "Money in"];
    public string Currency => _workspace.Settings.Currency;

    [ObservableProperty] public partial IReadOnlyList<CategoryOption> CategoryFilters { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<MonthOption> MonthFilters { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TransactionRowViewModel> VisibleRows { get; set; } = [];
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial CategoryOption? SelectedCategoryFilter { get; set; }
    [ObservableProperty] public partial MonthOption? SelectedMonthFilter { get; set; }
    [ObservableProperty] public partial string? SelectedDirection { get; set; }
    [ObservableProperty] public partial bool HasTransactions { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "";
    [ObservableProperty] public partial decimal MoneyOut { get; set; }
    [ObservableProperty] public partial decimal MoneyIn { get; set; }
    [ObservableProperty] public partial bool IsFiltered { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCorrection))]
    public partial CorrectionPrompt? Correction { get; set; }

    [ObservableProperty] public partial bool ShowCategoryTip { get; set; }

    public bool HasCorrection => Correction is not null;
    public bool HasNoMatches => HasTransactions && VisibleRows.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryFilterChanged(CategoryOption? value) => ApplyFilter();
    partial void OnSelectedMonthFilterChanged(MonthOption? value) => ApplyFilter();
    partial void OnSelectedDirectionChanged(string? value) => ApplyFilter();

    [RelayCommand]
    private void GoToImport() => _navigator.GoTo(Page.Import);

    [RelayCommand]
    private void ClearFilters()
    {
        _batchFilter = true;
        SearchText = "";
        SelectedCategoryFilter = AllCategories;
        SelectedMonthFilter = AllTime;
        SelectedDirection = Directions[0];
        _batchFilter = false;
        ApplyFilter();
    }

    [RelayCommand]
    private async Task DismissCategoryTip()
    {
        ShowCategoryTip = false;
        await _tips.DismissAsync(TipKeys.CategoryEdit);
    }

    [RelayCommand]
    private async Task AcceptCorrection()
    {
        if (Correction is not { } prompt)
            return;

        Correction = null;
        int changed;
        _updating = true;
        try
        {
            changed = await _workspace.AddRuleAndApplyAsync(prompt.Rule);
        }
        finally
        {
            _updating = false;
        }

        foreach (var row in _allRows)
            row.SyncFromModel();
        ApplyFilter();

        var category = _workspace.FindCategory(prompt.Rule.CategoryId)?.Name ?? "that category";
        _toasts.Show(changed > 0
            ? $"Got it. {Display.Count(changed, "other transaction")} moved to {category}, and new ones will follow."
            : $"Got it. We'll put \"{prompt.Rule.Pattern}\" in {category} from now on.");
    }

    [RelayCommand]
    private void DismissCorrection() => Correction = null;

    private void ApplyExternalFilter(TransactionFilter filter)
    {
        _batchFilter = true;
        SearchText = filter.Search ?? "";
        SelectedCategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == (filter.CategoryId ?? "")) ?? AllCategories;
        SelectedMonthFilter = MonthFilters.FirstOrDefault(m => m.Month == filter.Month) ?? AllTime;
        SelectedDirection = Directions[0];
        _batchFilter = false;
        ApplyFilter();
    }

    private async Task OnRowCategoryChanged(TransactionRowViewModel row, CategoryOption category)
    {
        var previous = row.Transaction.CategoryId;
        _updating = true;
        try
        {
            await _workspace.SetCategoryAsync(row.Transaction, category.Id);
        }
        finally
        {
            _updating = false;
        }

        var rule = _workspace.CreateCorrectionRule(row.Transaction, category.Id);
        var others = _workspace.CountAffectedBy(rule);
        var message = others > 0
            ? $"Always put \"{rule.Pattern}\" in {category.Name}? This also updates {Display.Count(others, "other transaction")}."
            : $"Always put \"{rule.Pattern}\" in {category.Name} from now on?";
        Correction = new CorrectionPrompt(rule, message);

        _toasts.Show($"Moved to {category.Name}.", ToastKind.Info, "Undo", async () =>
        {
            _updating = true;
            try
            {
                await _workspace.SetCategoryAsync(row.Transaction, previous);
            }
            finally
            {
                _updating = false;
            }

            row.SyncFromModel();
            if (Correction?.Rule == rule)
                Correction = null;
            ApplyFilter();
        });

        if (ShowCategoryTip)
            await DismissCategoryTip();
    }

    private void Rebuild()
    {
        var categories = _workspace.Categories.Select(CategoryOption.From).ToList();
        var currency = _workspace.Settings.Currency;
        OnPropertyChanged(nameof(Currency));

        _allRows = _workspace.Transactions
            .OrderByDescending(t => t.Date)
            .Select(t => new TransactionRowViewModel(t, categories, currency, OnRowCategoryChanged))
            .ToList();

        _batchFilter = true;
        var selectedId = SelectedCategoryFilter?.Id ?? "";
        CategoryFilters = [AllCategories, .. categories];
        SelectedCategoryFilter = CategoryFilters.FirstOrDefault(c => c.Id == selectedId) ?? AllCategories;

        var selectedMonth = SelectedMonthFilter?.Month;
        var months = _workspace.Transactions.Select(t => YearMonth.From(t.Date)).Distinct().OrderDescending();
        MonthFilters = [AllTime, .. months.Select(m => new MonthOption(m.ToString(), m))];
        SelectedMonthFilter = MonthFilters.FirstOrDefault(m => m.Month == selectedMonth) ?? AllTime;
        _batchFilter = false;

        HasTransactions = _allRows.Count > 0;
        ShowCategoryTip = HasTransactions && !_tips.IsDismissed(TipKeys.CategoryEdit);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_batchFilter)
            return;

        IEnumerable<TransactionRowViewModel> rows = _allRows;

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            rows = rows.Where(r =>
                r.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.RawDescription.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.AmountText.Replace(MoneyFormatter.Space, ' ').Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedCategoryFilter is { Id.Length: > 0 } category)
            rows = rows.Where(r => r.Transaction.CategoryId == category.Id);

        if (SelectedMonthFilter?.Month is { } month)
            rows = rows.Where(r => month.Contains(r.Transaction.Date));

        if (SelectedDirection == Directions[1])
            rows = rows.Where(r => r.Transaction.Amount < 0);
        else if (SelectedDirection == Directions[2])
            rows = rows.Where(r => r.Transaction.Amount > 0);

        VisibleRows = rows.ToList();
        MoneyOut = -VisibleRows.Where(r => r.Transaction.Amount < 0).Sum(r => r.Transaction.Amount);
        MoneyIn = VisibleRows.Where(r => r.Transaction.Amount > 0).Sum(r => r.Transaction.Amount);
        IsFiltered = VisibleRows.Count != _allRows.Count;
        ResultSummary = IsFiltered
            ? $"Showing {VisibleRows.Count:#,0} of {Display.Count(_allRows.Count, "transaction")}"
            : Display.Count(_allRows.Count, "transaction");
        OnPropertyChanged(nameof(HasNoMatches));
    }
}
