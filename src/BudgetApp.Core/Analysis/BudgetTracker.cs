using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public static class BudgetTracker
{
    private const double WatchFraction = 0.85;
    private const decimal OverTolerance = 1.02m;

    // For a month in progress, the month-end projection is what's already spent, plus everyday
    // spending for the days left, plus any recurring payments still due. "Everyday spending for the
    // days left" comes from the category's usual month when there's history (so lumpy categories like
    // fuel, bought twice a month, aren't flagged after the first fill-up), otherwise from this month's pace.
    public static BudgetSummary Evaluate(
        IReadOnlyList<CategoryBudget> budgets,
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<RecurringCharge> recurring,
        YearMonth month,
        DateOnly asOf,
        bool isLatestMonth)
    {
        var inProgress = isLatestMonth && month.Contains(asOf) && asOf.Day < month.DaysInMonth;
        var daysElapsed = inProgress ? asOf.Day : month.DaysInMonth;
        var daysLeft = month.DaysInMonth - daysElapsed;
        var byId = categories.ToDictionary(c => c.Id);
        var recurringIds = recurring.SelectMany(r => r.TransactionIds).ToHashSet();
        var history = inProgress ? EverydayHistory(transactions, recurringIds, month) : new Dictionary<string, decimal>();

        var items = new List<BudgetStatus>();
        foreach (var budget in budgets)
        {
            if (!byId.TryGetValue(budget.CategoryId, out var category) || budget.Amount <= 0)
                continue;

            var inCategory = transactions.Where(t => t.CategoryId == budget.CategoryId && month.Contains(t.Date)).ToList();
            var spent = Math.Max(0, -inCategory.Sum(t => t.Amount));
            var projected = spent;

            if (inProgress)
            {
                var remainingEveryday = history.TryGetValue(budget.CategoryId, out var usualMonth)
                    ? usualMonth * daysLeft / month.DaysInMonth
                    : Math.Max(0, -inCategory.Where(t => !recurringIds.Contains(t.Id)).Sum(t => t.Amount)) / daysElapsed * daysLeft;
                var stillDue = recurring
                    .Where(r => r.CategoryId == budget.CategoryId)
                    .Sum(r => DueBetween(r, asOf, month.Last) * r.Amount);
                projected = decimal.Round(spent + remainingEveryday + stillDue, 2);
            }

            // Watch means "act now": heading over, or nearly spent with more spending still expected.
            // A finished month is simply over or not.
            var used = (double)(spent / budget.Amount);
            var moreExpected = projected > spent * 1.01m;
            var state = spent > budget.Amount ? BudgetState.Over
                : inProgress && (projected > budget.Amount * OverTolerance || (used >= WatchFraction && moreExpected)) ? BudgetState.Watch
                : BudgetState.OnTrack;

            items.Add(new BudgetStatus(category.Id, category.Name, category.Colour, category.Icon, budget.Amount, spent, projected, state));
        }

        var ordered = items
            .OrderByDescending(i => i.State)
            .ThenByDescending(i => i.ProjectedFraction)
            .ToList();

        return new BudgetSummary(
            ordered,
            ordered.Sum(i => i.Limit),
            ordered.Sum(i => i.Spent),
            ordered.Sum(i => i.Projected),
            daysLeft,
            inProgress);
    }

    // Suggested limits: the average of the last three complete months, rounded up to a tidy step.
    public static IReadOnlyList<BudgetSuggestion> Suggest(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<CategoryBudget> existing)
    {
        if (transactions.Count == 0)
            return [];

        var asOf = transactions.Max(t => t.Date);
        var latest = YearMonth.From(asOf);
        var lastComplete = asOf.Day < latest.DaysInMonth ? latest.AddMonths(-1) : latest;
        var months = transactions.Select(t => YearMonth.From(t.Date)).Distinct()
            .Where(m => m <= lastComplete)
            .OrderDescending()
            .Take(3)
            .ToList();

        // Fall back to the month in progress if there's nothing complete yet.
        if (months.Count == 0)
            months = [latest];

        var budgeted = existing.Select(b => b.CategoryId).ToHashSet();
        var analyzer = new SpendingAnalyzer(categories);
        var totals = months
            .SelectMany(m => analyzer.CategoryTotals(transactions, m))
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return categories
            .Where(c => !budgeted.Contains(c.Id) && c.Id is not (CategoryIds.Income or CategoryIds.Transfers or CategoryIds.Uncategorised))
            .Select(c => (Category: c, Average: totals.GetValueOrDefault(c.Id) / months.Count))
            .Where(x => x.Average >= 50)
            .Select(x => new BudgetSuggestion(x.Category.Id, x.Category.Name, x.Category.Colour, x.Category.Icon,
                RoundUp(x.Average), decimal.Round(x.Average, 2), months.Count))
            .OrderByDescending(s => s.Amount)
            .ToList();
    }

    public static decimal RoundUp(decimal amount)
    {
        var step = amount switch
        {
            < 1_000 => 50m,
            < 5_000 => 100m,
            _ => 500m,
        };
        return Math.Ceiling(amount / step) * step;
    }

    // Average non-recurring spend per category over up to three complete months before this one.
    // Needs at least two months to be trusted.
    private static Dictionary<string, decimal> EverydayHistory(IReadOnlyList<Transaction> transactions, ISet<Guid> recurringIds, YearMonth month)
    {
        var months = transactions.Select(t => YearMonth.From(t.Date)).Distinct()
            .Where(m => m < month)
            .OrderDescending()
            .Take(3)
            .ToList();
        if (months.Count < 2)
            return [];

        return transactions
            .Where(t => t.Amount < 0 && !recurringIds.Contains(t.Id) && months.Contains(YearMonth.From(t.Date)))
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => -g.Sum(t => t.Amount) / months.Count);
    }

    private static int DueBetween(RecurringCharge charge, DateOnly after, DateOnly until)
    {
        var count = 0;
        for (var date = charge.NextDate; date <= until; date = RecurringDetector.NextDate(date, charge.Cadence))
        {
            if (date > after)
                count++;
        }

        return count;
    }
}
