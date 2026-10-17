using System.Text;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Analysis;
using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Models;

namespace BudgetApp.Core.Services;

// Everything lives in memory after startup; each mutation persists the affected file.
public sealed class BudgetWorkspace
{
    private static readonly HashSet<string> ProtectedCategories =
        [CategoryIds.Uncategorised, CategoryIds.Income, CategoryIds.Transfers];

    private readonly IDataStore _store;
    private readonly RuleBasedCategoriser _categoriser;
    private readonly List<Transaction> _transactions = [];
    private readonly List<Category> _categories = [];
    private readonly List<CategoryRule> _rules = [];
    private readonly List<Account> _accounts = [];
    private readonly List<CategoryBudget> _budgets = [];
    private readonly HashSet<string> _hashes = new(StringComparer.Ordinal);

    public BudgetWorkspace(IDataStore store, RuleBasedCategoriser categoriser)
    {
        _store = store;
        _categoriser = categoriser;
    }

    public event EventHandler? TransactionsChanged;
    public event EventHandler? CategoriesChanged;
    public event EventHandler? RulesChanged;
    public event EventHandler? SettingsChanged;
    public event EventHandler? BudgetsChanged;

    public IReadOnlyList<Transaction> Transactions => _transactions;
    public IReadOnlyList<Category> Categories => _categories;
    public IReadOnlyList<CategoryRule> Rules => _rules;
    public IReadOnlyList<Account> Accounts => _accounts;
    public IReadOnlyList<CategoryBudget> Budgets => _budgets;
    public AppSettings Settings { get; private set; } = new();
    public ICategoriser Categoriser => _categoriser;
    public string DataLocation => _store.Location;
    public Account DefaultAccount => _accounts[0];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var data = await _store.LoadAsync(ct);

        Reset(_transactions, data.Transactions);
        Reset(_categories, data.Categories);
        Reset(_rules, data.Rules);
        Reset(_accounts, data.Accounts);
        Reset(_budgets, data.Budgets);
        Settings = data.Settings;

        _hashes.Clear();
        _hashes.UnionWith(_transactions.Select(t => t.ImportHash));

        if (_categories.Count == 0)
        {
            _categories.AddRange(BuiltInCatalog.CreateCategories());
            await _store.SaveCategoriesAsync(_categories.ToArray(), ct);
        }

        if (_rules.Count == 0)
        {
            _rules.AddRange(BuiltInCatalog.CreateRules());
            await _store.SaveRulesAsync(_rules.ToArray(), ct);
        }

        if (_accounts.Count == 0)
        {
            _accounts.Add(new Account { Name = "Main account" });
            await _store.SaveAccountsAsync(_accounts.ToArray(), ct);
        }

        await MigrateAsync(ct);
        _categoriser.SetRules(_rules);

        if (UpdateRecurringFlags())
            await SaveTransactionsAsync(ct);
    }

    // Reloads from disk (after a restore, for example) and tells every screen to refresh.
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
        CategoriesChanged?.Invoke(this, EventArgs.Empty);
        RulesChanged?.Invoke(this, EventArgs.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        BudgetsChanged?.Invoke(this, EventArgs.Empty);
        TransactionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Category? FindCategory(string id) => _categories.FirstOrDefault(c => c.Id == id);

    public bool IsProtected(Category category) => ProtectedCategories.Contains(category.Id);

    public bool ContainsHash(string importHash) => _hashes.Contains(importHash);

    // Transactions

    public async Task<int> AddTransactionsAsync(IEnumerable<Transaction> transactions, CancellationToken ct = default)
    {
        var added = 0;
        foreach (var t in transactions)
        {
            if (!_hashes.Add(t.ImportHash))
                continue;
            _transactions.Add(t);
            added++;
        }

        if (added > 0)
        {
            UpdateRecurringFlags();
            await SaveTransactionsAsync(ct);
            TransactionsChanged?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    public async Task SetCategoryAsync(Transaction transaction, string categoryId, CancellationToken ct = default)
    {
        if (transaction.CategoryId == categoryId)
            return;

        transaction.CategoryId = categoryId;
        await SaveTransactionsAsync(ct);
        TransactionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearTransactionsAsync(CancellationToken ct = default)
    {
        _transactions.Clear();
        _hashes.Clear();
        await SaveTransactionsAsync(ct);
        TransactionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Categories

    public async Task<Category> AddCategoryAsync(string name, string icon, string colour, bool isEssential, CancellationToken ct = default)
    {
        var category = new Category
        {
            Id = UniqueCategoryId(name),
            Name = name.Trim(),
            Icon = icon,
            Colour = colour,
            IsEssential = isEssential,
        };

        _categories.Insert(Math.Max(0, _categories.FindIndex(c => c.Id == CategoryIds.Uncategorised)), category);
        await _store.SaveCategoriesAsync(_categories.ToArray(), ct);
        CategoriesChanged?.Invoke(this, EventArgs.Empty);
        return category;
    }

    public async Task UpdateCategoryAsync(Category category, CancellationToken ct = default)
    {
        await _store.SaveCategoriesAsync(_categories.ToArray(), ct);
        CategoriesChanged?.Invoke(this, EventArgs.Empty);
        TransactionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> DeleteCategoryAsync(string categoryId, CancellationToken ct = default)
    {
        var category = FindCategory(categoryId);
        if (category is null || IsProtected(category))
            return 0;

        var moved = 0;
        foreach (var t in _transactions.Where(t => t.CategoryId == categoryId))
        {
            t.CategoryId = CategoryIds.Uncategorised;
            moved++;
        }

        _categories.Remove(category);
        _rules.RemoveAll(r => r.CategoryId == categoryId);
        _categoriser.SetRules(_rules);
        if (_budgets.RemoveAll(b => b.CategoryId == categoryId) > 0)
            await SaveBudgetsAsync(ct);

        await _store.SaveCategoriesAsync(_categories.ToArray(), ct);
        await _store.SaveRulesAsync(_rules.ToArray(), ct);
        if (moved > 0)
            await SaveTransactionsAsync(ct);

        CategoriesChanged?.Invoke(this, EventArgs.Empty);
        RulesChanged?.Invoke(this, EventArgs.Empty);
        TransactionsChanged?.Invoke(this, EventArgs.Empty);
        return moved;
    }

    // Rules

    public CategoryRule CreateCorrectionRule(Transaction transaction, string categoryId) => new()
    {
        Pattern = string.IsNullOrWhiteSpace(transaction.CleanDescription) ? transaction.Description : transaction.CleanDescription,
        MatchType = RuleMatchType.Contains,
        CategoryId = categoryId,
        Priority = 1000,
        Source = RuleSource.UserCorrection,
    };

    public int CountAffectedBy(CategoryRule rule) =>
        _transactions.Count(t => t.CategoryId != rule.CategoryId && RuleBasedCategoriser.Matches(rule, CategorisationInput.From(t)));

    public int CountMatching(CategoryRule rule) =>
        _transactions.Count(t => RuleBasedCategoriser.Matches(rule, CategorisationInput.From(t)));

    public async Task<int> AddRuleAndApplyAsync(CategoryRule rule, CancellationToken ct = default)
    {
        _rules.RemoveAll(r => r.Source == RuleSource.UserCorrection &&
                              string.Equals(r.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase));
        _rules.Add(rule);
        _categoriser.SetRules(_rules);

        var changed = 0;
        foreach (var t in _transactions)
        {
            if (t.CategoryId != rule.CategoryId && RuleBasedCategoriser.Matches(rule, CategorisationInput.From(t)))
            {
                t.CategoryId = rule.CategoryId;
                changed++;
            }
        }

        await _store.SaveRulesAsync(_rules.ToArray(), ct);
        if (changed > 0)
            await SaveTransactionsAsync(ct);

        RulesChanged?.Invoke(this, EventArgs.Empty);
        if (changed > 0)
            TransactionsChanged?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    public async Task RemoveRuleAsync(CategoryRule rule, CancellationToken ct = default)
    {
        if (!_rules.Remove(rule))
            return;

        _categoriser.SetRules(_rules);
        await _store.SaveRulesAsync(_rules.ToArray(), ct);
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RestoreRuleAsync(CategoryRule rule, CancellationToken ct = default)
    {
        if (_rules.Contains(rule))
            return;

        _rules.Add(rule);
        _categoriser.SetRules(_rules);
        await _store.SaveRulesAsync(_rules.ToArray(), ct);
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Runs the current rules over existing transactions. By default only uncategorised ones are
    // touched, so manual choices are never overwritten.
    public async Task<int> ReapplyRulesAsync(bool onlyUncategorised = true, CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var t in _transactions)
        {
            if (onlyUncategorised && t.CategoryId != CategoryIds.Uncategorised)
                continue;

            var match = _categoriser.Categorise(CategorisationInput.From(t));
            if (match.CategoryId != t.CategoryId)
            {
                t.CategoryId = match.CategoryId;
                changed++;
            }
        }

        if (changed > 0)
        {
            await SaveTransactionsAsync(ct);
            TransactionsChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    // Budgets

    public CategoryBudget? FindBudget(string categoryId) => _budgets.FirstOrDefault(b => b.CategoryId == categoryId);

    public async Task<CategoryBudget> SetBudgetAsync(string categoryId, decimal amount, CancellationToken ct = default)
    {
        var budget = FindBudget(categoryId);
        if (budget is null)
        {
            budget = new CategoryBudget { CategoryId = categoryId };
            _budgets.Add(budget);
        }

        budget.Amount = Math.Max(0, decimal.Round(amount, 2));
        await SaveBudgetsAsync(ct);
        return budget;
    }

    public async Task<int> AddBudgetsAsync(IEnumerable<CategoryBudget> budgets, CancellationToken ct = default)
    {
        var added = 0;
        foreach (var budget in budgets)
        {
            if (FindBudget(budget.CategoryId) is not null || FindCategory(budget.CategoryId) is null)
                continue;
            _budgets.Add(budget);
            added++;
        }

        if (added > 0)
            await SaveBudgetsAsync(ct);
        return added;
    }

    public async Task<CategoryBudget?> RemoveBudgetAsync(string categoryId, CancellationToken ct = default)
    {
        var budget = FindBudget(categoryId);
        if (budget is null)
            return null;

        _budgets.Remove(budget);
        await SaveBudgetsAsync(ct);
        return budget;
    }

    private async Task SaveBudgetsAsync(CancellationToken ct)
    {
        await _store.SaveBudgetsAsync(_budgets.ToArray(), ct);
        BudgetsChanged?.Invoke(this, EventArgs.Empty);
    }

    // Settings

    public async Task SaveSettingsAsync(CancellationToken ct = default)
    {
        await _store.SaveSettingsAsync(Settings, ct);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool UpdateRecurringFlags()
    {
        if (_transactions.Count == 0)
            return false;

        var asOf = _transactions.Max(t => t.Date);
        var recurringIds = RecurringDetector.Detect(_transactions, asOf).SelectMany(r => r.TransactionIds).ToHashSet();

        var changed = false;
        foreach (var t in _transactions)
        {
            var isRecurring = recurringIds.Contains(t.Id);
            if (t.IsRecurring != isRecurring)
            {
                t.IsRecurring = isRecurring;
                changed = true;
            }
        }

        return changed;
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        if (Settings.DataVersion >= AppSettings.CurrentDataVersion)
            return;

        // v2: built-in categories moved to the 12-colour palette so no two share a colour.
        if (Settings.DataVersion < 2)
        {
            var defaults = BuiltInCatalog.CreateCategories().ToDictionary(c => c.Id);
            foreach (var category in _categories.Where(c => c.IsBuiltIn))
            {
                if (defaults.TryGetValue(category.Id, out var fresh))
                    category.Colour = fresh.Colour;
            }

            await _store.SaveCategoriesAsync(_categories.ToArray(), ct);
        }

        Settings.DataVersion = AppSettings.CurrentDataVersion;
        await _store.SaveSettingsAsync(Settings, ct);
    }

    private string UniqueCategoryId(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        var baseId = slug.ToString().Trim('-');
        if (baseId.Length == 0)
            baseId = "category";

        var id = baseId;
        for (var n = 2; _categories.Any(c => c.Id == id); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    private Task SaveTransactionsAsync(CancellationToken ct) => _store.SaveTransactionsAsync(_transactions.ToArray(), ct);

    private static void Reset<T>(List<T> target, IEnumerable<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
