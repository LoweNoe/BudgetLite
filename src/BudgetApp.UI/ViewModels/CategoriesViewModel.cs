using System.Text.RegularExpressions;
using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed record CategoryListItem(Category Category, string Name, string Icon, string Colour, string Detail, string SpendText, bool IsEssential);

public sealed record RuleRow(CategoryRule Rule, string Pattern, string MatchText, string Detail, bool IsUserRule);

public sealed record MatchTypeOption(string Label, RuleMatchType Type);

public sealed partial class CategoriesViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> ColourTokens = Enumerable.Range(1, 12).Select(i => $"chart-{i}").ToList();

    public static readonly IReadOnlyList<string> IconKeys =
    [
        "cart", "utensils", "coffee", "car", "fuel", "plane", "repeat", "phone", "zap", "home", "landmark", "film",
        "bag", "shirt", "gift", "heart", "book", "target", "wallet", "arrows", "tag", "sparkles",
    ];

    private readonly BudgetWorkspace _workspace;
    private readonly ToastService _toasts;
    private readonly DialogService _dialogs;
    private readonly TipsService _tips;
    private bool _loadingEditor;

    public CategoriesViewModel(BudgetWorkspace workspace, ToastService toasts, DialogService dialogs, TipsService tips)
    {
        _workspace = workspace;
        _toasts = toasts;
        _dialogs = dialogs;
        _tips = tips;

        workspace.CategoriesChanged += (_, _) => Refresh();
        workspace.RulesChanged += (_, _) => Refresh();
        workspace.TransactionsChanged += (_, _) => Refresh();
        tips.TipsReset += (_, _) => ShowEssentialTip = !_tips.IsDismissed(TipKeys.CategoriesEssential);

        NewRuleType = MatchTypes[0];
        ShowEssentialTip = !_tips.IsDismissed(TipKeys.CategoriesEssential);
        Refresh();
    }

    public IReadOnlyList<string> Colours => ColourTokens;
    public IReadOnlyList<string> Icons => IconKeys;

    public IReadOnlyList<MatchTypeOption> MatchTypes { get; } =
    [
        new("Description contains", RuleMatchType.Contains),
        new("Regular expression", RuleMatchType.Regex),
        new("Card category code (MCC)", RuleMatchType.MerchantCategoryCode),
    ];

    [ObservableProperty] public partial IReadOnlyList<CategoryListItem> Items { get; set; } = [];
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool ShowEssentialTip { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor), nameof(EditorTitle), nameof(SaveText), nameof(CanDelete), nameof(ShowRules))]
    public partial CategoryListItem? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor), nameof(EditorTitle), nameof(SaveText), nameof(CanDelete), nameof(ShowRules))]
    public partial bool IsCreating { get; set; }

    // Editor fields
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? EditColour { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? EditIcon { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool EditEssential { get; set; }

    [ObservableProperty] public partial IReadOnlyList<RuleRow> UserRules { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<RuleRow> BuiltInRules { get; set; } = [];
    [ObservableProperty] public partial string BuiltInRulesHeader { get; set; } = "";

    // New rule form
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuleCommand))]
    public partial string NewRulePattern { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuleCommand))]
    public partial MatchTypeOption? NewRuleType { get; set; }

    [ObservableProperty] public partial string NewRuleFeedback { get; set; } = "";
    [ObservableProperty] public partial bool NewRuleInvalid { get; set; }

    public bool HasEditor => SelectedItem is not null || IsCreating;
    public bool ShowRules => SelectedItem is not null && !IsCreating;
    public string EditorTitle => IsCreating ? "New category" : SelectedItem?.Name ?? "";
    public string SaveText => IsCreating ? "Create category" : "Save changes";
    public bool CanDelete => !IsCreating && SelectedItem is { } item && !_workspace.IsProtected(item.Category);
    public bool HasUserRules => UserRules.Count > 0;
    public bool HasBuiltInRules => BuiltInRules.Count > 0;

    partial void OnSelectedItemChanged(CategoryListItem? value)
    {
        if (value is null || _refreshing)
            return;

        IsCreating = false;
        LoadEditor(value.Category);
    }

    partial void OnNewRulePatternChanged(string value) => UpdateRuleFeedback();
    partial void OnNewRuleTypeChanged(MatchTypeOption? value) => UpdateRuleFeedback();

    [RelayCommand]
    private void NewCategory()
    {
        SelectedItem = null;
        IsCreating = true;
        _loadingEditor = true;
        EditName = "";
        EditColour = ColourTokens[(Items.Count + 3) % ColourTokens.Count];
        EditIcon = "tag";
        EditEssential = false;
        _loadingEditor = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsCreating = false;
        SelectedItem = null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var name = EditName.Trim();
        if (IsCreating)
        {
            var created = await _workspace.AddCategoryAsync(name, EditIcon ?? "tag", EditColour ?? "chart-1", EditEssential);
            IsCreating = false;
            SelectedItem = Items.FirstOrDefault(i => i.Category.Id == created.Id);
            _toasts.Show($"{name} created. Add a rule so we can fill it automatically.");
            return;
        }

        if (SelectedItem?.Category is not { } category)
            return;

        category.Name = name;
        category.Colour = EditColour ?? category.Colour;
        category.Icon = EditIcon ?? category.Icon;
        category.IsEssential = EditEssential;
        await _workspace.UpdateCategoryAsync(category);
        _toasts.Show($"{name} saved.");
    }

    private bool CanSave()
    {
        if (_loadingEditor || string.IsNullOrWhiteSpace(EditName))
            return false;
        if (IsCreating)
            return true;
        return SelectedItem?.Category is { } c &&
               (c.Name != EditName.Trim() || c.Colour != EditColour || c.Icon != EditIcon || c.IsEssential != EditEssential);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedItem?.Category is not { } category || _workspace.IsProtected(category))
            return;

        var count = _workspace.Transactions.Count(t => t.CategoryId == category.Id);
        var rules = _workspace.Rules.Count(r => r.CategoryId == category.Id);
        var detail = count > 0
            ? $"{Display.Count(count, "transaction")} will move to Uncategorised and {Display.Count(rules, "rule")} will be removed."
            : $"{Display.Count(rules, "rule")} will be removed.";

        if (!await _dialogs.ConfirmAsync($"Delete {category.Name}?", detail, "Delete category", destructive: true))
            return;

        SelectedItem = null;
        await _workspace.DeleteCategoryAsync(category.Id);
        _toasts.Show($"{category.Name} deleted.");
    }

    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private async Task AddRule()
    {
        if (SelectedItem?.Category is not { } category || BuildRule(category.Id) is not { } rule)
            return;

        var moved = await _workspace.AddRuleAndApplyAsync(rule);
        NewRulePattern = "";
        _toasts.Show(moved > 0
            ? $"Rule added. {Display.Count(moved, "transaction")} moved to {category.Name}."
            : $"Rule added. Matching transactions will go to {category.Name}.");
    }

    private bool CanAddRule() => SelectedItem is not null && !string.IsNullOrWhiteSpace(NewRulePattern) && !NewRuleInvalid;

    [RelayCommand]
    private async Task RemoveRule(RuleRow? row)
    {
        if (row is null)
            return;

        await _workspace.RemoveRuleAsync(row.Rule);
        _toasts.Show($"Removed the rule for \"{row.Pattern}\".", ToastKind.Info, "Undo", () => _workspace.RestoreRuleAsync(row.Rule));
    }

    [RelayCommand]
    private async Task ReapplyRules()
    {
        var sorted = await _workspace.ReapplyRulesAsync();
        _toasts.Show(sorted > 0
            ? $"Sorted {Display.Count(sorted, "transaction")} using your rules."
            : "Nothing new to sort. Uncategorised transactions don't match any rule yet.",
            sorted > 0 ? ToastKind.Success : ToastKind.Info);
    }

    [RelayCommand]
    private async Task DismissEssentialTip()
    {
        ShowEssentialTip = false;
        await _tips.DismissAsync(TipKeys.CategoriesEssential);
    }

    private void Refresh()
    {
        var selectedId = SelectedItem?.Category.Id;
        var months = _workspace.Transactions.Select(t => YearMonth.From(t.Date)).ToList();
        var latest = months.Count > 0 ? months.Max() : (YearMonth?)null;
        var totals = latest is { } month
            ? new SpendingAnalyzer(_workspace.Categories).CategoryTotals(_workspace.Transactions, month).ToDictionary(t => t.CategoryId, t => t.Amount)
            : [];
        var textRules = _workspace.Rules.Where(r => r.MatchType != RuleMatchType.MerchantCategoryCode).ToList();

        Items = _workspace.Categories
            .Select(c =>
            {
                var count = textRules.Count(r => r.CategoryId == c.Id);
                var spend = totals.GetValueOrDefault(c.Id);
                return new CategoryListItem(
                    c,
                    c.Name,
                    c.Icon,
                    c.Colour,
                    $"{(c.IsEssential ? "Essential" : "Non-essential")} · {(count == 0 ? "no rules" : Display.Count(count, "rule"))}",
                    spend > 0 ? MoneyFormatter.Format(spend, _workspace.Settings.Currency, wholeUnits: true) : "",
                    c.IsEssential);
            })
            .ToList();

        var userRules = _workspace.Rules.Count(r => r.Source == RuleSource.UserCorrection);
        Summary = $"{Display.Count(Items.Count, "category", "categories")} · {Display.Count(userRules, "rule")} of your own"
                  + (latest is { } l ? $" · spending shown for {l}" : "");

        // Keep the selection across refreshes without wiping any unsaved edits in the editor.
        if (selectedId is not null && Items.FirstOrDefault(i => i.Category.Id == selectedId) is { } match)
        {
            _refreshing = true;
            SelectedItem = match;
            _refreshing = false;
            LoadRules(match.Category);
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _refreshing;

    private void LoadEditor(Category category)
    {
        _loadingEditor = true;
        EditName = category.Name;
        EditColour = category.Colour;
        EditIcon = category.Icon;
        EditEssential = category.IsEssential;
        _loadingEditor = false;
        SaveCommand.NotifyCanExecuteChanged();
        NewRulePattern = "";
        LoadRules(category);
    }

    private void LoadRules(Category category)
    {
        var rules = _workspace.Rules.Where(r => r.CategoryId == category.Id).ToList();

        UserRules = rules.Where(r => r.Source == RuleSource.UserCorrection).Select(ToRow).ToList();
        BuiltInRules = rules.Where(r => r.Source == RuleSource.BuiltIn)
            .OrderBy(r => r.MatchType)
            .ThenBy(r => r.Pattern)
            .Select(ToRow)
            .ToList();
        BuiltInRulesHeader = $"Built-in rules ({BuiltInRules.Count})";
        OnPropertyChanged(nameof(HasUserRules));
        OnPropertyChanged(nameof(HasBuiltInRules));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(EditorTitle));
    }

    private RuleRow ToRow(CategoryRule rule)
    {
        var matches = _workspace.CountMatching(rule);
        var direction = rule.AppliesTo switch
        {
            AmountDirection.Debits => "money out",
            AmountDirection.Credits => "money in",
            _ => "in or out",
        };
        var match = rule.MatchType switch
        {
            RuleMatchType.Regex => "Pattern",
            RuleMatchType.MerchantCategoryCode => "Card code",
            _ => "Contains",
        };
        return new RuleRow(rule, rule.Pattern, match, $"{direction} · {(matches == 0 ? "no matches yet" : Display.Count(matches, "match", "matches"))}",
            rule.Source == RuleSource.UserCorrection);
    }

    private CategoryRule? BuildRule(string categoryId)
    {
        var type = NewRuleType?.Type ?? RuleMatchType.Contains;
        var pattern = NewRulePattern.Trim();
        if (pattern.Length == 0 || !IsValid(pattern, type))
            return null;

        return new CategoryRule
        {
            Pattern = type == RuleMatchType.Contains ? pattern.ToUpperInvariant() : pattern,
            MatchType = type,
            CategoryId = categoryId,
            Priority = 1000,
            Source = RuleSource.UserCorrection,
        };
    }

    private void UpdateRuleFeedback()
    {
        var pattern = NewRulePattern.Trim();
        var type = NewRuleType?.Type ?? RuleMatchType.Contains;

        if (pattern.Length == 0)
        {
            NewRuleInvalid = false;
            NewRuleFeedback = type switch
            {
                RuleMatchType.MerchantCategoryCode => "Enter a code like 5814, or a range like 5811-5814.",
                RuleMatchType.Regex => "For advanced matching, e.g. ^PNP|PICK N PAY.",
                _ => "Words from the description, e.g. VIRGIN ACTIVE.",
            };
        }
        else if (!IsValid(pattern, type))
        {
            NewRuleInvalid = true;
            NewRuleFeedback = type == RuleMatchType.MerchantCategoryCode ? "That isn't a card code or range." : "That pattern isn't valid.";
        }
        else
        {
            NewRuleInvalid = false;
            var count = SelectedItem is { } item && BuildRule(item.Category.Id) is { } rule ? _workspace.CountMatching(rule) : 0;
            NewRuleFeedback = count == 0 ? "No transactions match yet." : $"Matches {Display.Count(count, "transaction")}.";
        }

        AddRuleCommand.NotifyCanExecuteChanged();
    }

    private static bool IsValid(string pattern, RuleMatchType type)
    {
        switch (type)
        {
            case RuleMatchType.Regex:
                try
                {
                    _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }

            case RuleMatchType.MerchantCategoryCode:
                return MccPattern().IsMatch(pattern);

            default:
                return true;
        }
    }

    [GeneratedRegex(@"^\d{4}(\s*-\s*\d{4})?$")]
    private static partial Regex MccPattern();
}
