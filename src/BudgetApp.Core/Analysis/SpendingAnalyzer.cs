using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

// Spending is measured per category as money out minus refunds, so a R 350 refund from
// Takealot nets off against Shopping. Transfers are moving money, not spending, and are left out.
public sealed class SpendingAnalyzer
{
    private readonly Dictionary<string, Category> _categories;

    public SpendingAnalyzer(IEnumerable<Category> categories)
    {
        _categories = categories.ToDictionary(c => c.Id);
    }

    public static bool CountsAsSpending(Transaction t) =>
        t.CategoryId is not (CategoryIds.Income or CategoryIds.Transfers);

    // throughDay limits the month to its first N days, for like-for-like comparisons with a month in progress.
    public MonthSummary Summarise(IEnumerable<Transaction> transactions, YearMonth month, int? throughDay = null)
    {
        var inMonth = transactions.Where(t => month.Contains(t.Date) && (throughDay is null || t.Date.Day <= throughDay)).ToList();
        if (inMonth.Count == 0)
            return MonthSummary.Empty(month);

        var byCategory = NetSpendByCategory(inMonth);
        decimal Sum(Func<string, bool> filter) => byCategory.Where(kv => filter(kv.Key)).Sum(kv => kv.Value);

        return new MonthSummary(
            month,
            Spent: byCategory.Values.Sum(),
            Income: inMonth.Where(t => t.CategoryId == CategoryIds.Income && t.Amount > 0).Sum(t => t.Amount),
            Essential: Sum(id => id != CategoryIds.Uncategorised && IsEssential(id)),
            NonEssential: Sum(id => id != CategoryIds.Uncategorised && !IsEssential(id)),
            Uncategorised: Sum(id => id == CategoryIds.Uncategorised),
            BankFees: Sum(id => id == CategoryIds.BankFees),
            TransactionCount: inMonth.Count);
    }

    public IReadOnlyList<CategoryTotal> CategoryTotals(IEnumerable<Transaction> transactions, YearMonth month, int? previousThroughDay = null)
    {
        var all = transactions as IReadOnlyCollection<Transaction> ?? transactions.ToList();
        var current = NetSpendByCategory(all.Where(t => month.Contains(t.Date)));
        var previousMonth = month.AddMonths(-1);
        var previous = NetSpendByCategory(all.Where(t => previousMonth.Contains(t.Date) && (previousThroughDay is null || t.Date.Day <= previousThroughDay)));
        var counts = all.Where(t => month.Contains(t.Date) && CountsAsSpending(t) && t.Amount < 0)
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        return current.Keys.Union(previous.Keys)
            .Select(id =>
            {
                var category = _categories.GetValueOrDefault(id);
                return new CategoryTotal(
                    id,
                    category?.Name ?? "Uncategorised",
                    category?.Colour ?? "chart-8",
                    category?.Icon ?? "help",
                    category?.IsEssential ?? false,
                    current.GetValueOrDefault(id),
                    previous.GetValueOrDefault(id),
                    counts.GetValueOrDefault(id));
            })
            .Where(c => c.Amount > 0 || c.PreviousAmount > 0)
            .OrderByDescending(c => c.Amount)
            .ThenByDescending(c => c.PreviousAmount)
            .ToList();
    }

    public IReadOnlyList<MonthSummary> Trend(IEnumerable<Transaction> transactions, YearMonth endMonth, int months)
    {
        var all = transactions as IReadOnlyCollection<Transaction> ?? transactions.ToList();
        return Enumerable.Range(0, months)
            .Select(i => Summarise(all, endMonth.AddMonths(i - months + 1)))
            .ToList();
    }

    public decimal BankFeesBetween(IEnumerable<Transaction> transactions, DateOnly from, DateOnly to) =>
        Math.Max(0, -transactions
            .Where(t => t.CategoryId == CategoryIds.BankFees && t.Date >= from && t.Date <= to)
            .Sum(t => t.Amount));

    private bool IsEssential(string categoryId) => _categories.GetValueOrDefault(categoryId)?.IsEssential ?? false;

    private static Dictionary<string, decimal> NetSpendByCategory(IEnumerable<Transaction> transactions) =>
        transactions
            .Where(CountsAsSpending)
            .GroupBy(t => t.CategoryId)
            .Select(g => (g.Key, Net: -g.Sum(t => t.Amount)))
            .Where(x => x.Net > 0)
            .ToDictionary(x => x.Key, x => x.Net);
}
