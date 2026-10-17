using BudgetApp.Core.Models;

namespace BudgetApp.Core.Abstractions;

public interface IDataStore
{
    string Location { get; }

    Task<BudgetData> LoadAsync(CancellationToken ct = default);

    Task SaveTransactionsAsync(IReadOnlyList<Transaction> transactions, CancellationToken ct = default);
    Task SaveCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct = default);
    Task SaveRulesAsync(IReadOnlyList<CategoryRule> rules, CancellationToken ct = default);
    Task SaveAccountsAsync(IReadOnlyList<Account> accounts, CancellationToken ct = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);
    Task SaveBudgetsAsync(IReadOnlyList<CategoryBudget> budgets, CancellationToken ct = default);
}

public interface IBankTipsProvider
{
    Task<IReadOnlyList<BankTip>> GetTipsAsync(CancellationToken ct = default);
}
