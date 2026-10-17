using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public static class BudgetAnalyzer
{
    public const int TrendMonths = 6;

    public static AnalysisReport? Analyse(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        AppSettings settings,
        YearMonth? month = null,
        IReadOnlyList<CategoryBudget>? budgets = null)
    {
        if (transactions.Count == 0)
            return null;

        var months = transactions.Select(t => YearMonth.From(t.Date)).Distinct().Order().ToList();
        var latest = months[^1];
        var selected = month is { } m && months.Contains(m) ? m : latest;
        var asOf = transactions.Max(t => t.Date);

        // A month still in progress is compared with the same days of the month before.
        int? comparedThroughDay = selected == latest && asOf.Day < selected.DaysInMonth ? asOf.Day : null;

        var spending = new SpendingAnalyzer(categories);
        var summary = spending.Summarise(transactions, selected);
        var previousMonth = selected.AddMonths(-1);
        var previous = months.Contains(previousMonth) ? spending.Summarise(transactions, previousMonth, comparedThroughDay) : null;
        var categoryTotals = spending.CategoryTotals(transactions, selected, comparedThroughDay);
        var trend = spending.Trend(transactions, selected, TrendMonths);

        var recurring = RecurringDetector.Detect(transactions, asOf);
        var income = IncomeDetector.Detect(transactions, asOf, settings.WeekendPayday);
        var forecast = ForecastCalculator.Calculate(transactions, recurring, income, settings);
        var feesYearToDate = spending.BankFeesBetween(transactions, new DateOnly(selected.Year, 1, 1), selected.Last);

        var budgetSummary = budgets is { Count: > 0 }
            ? BudgetTracker.Evaluate(budgets, transactions, categories, recurring, selected, asOf, selected == latest)
            : null;

        var insights = new InsightGenerator(settings.Currency).Generate(
            selected,
            selected == latest,
            comparedThroughDay is not null,
            summary,
            previous,
            categoryTotals,
            recurring,
            forecast,
            feesYearToDate,
            transactions.Where(t => selected.Contains(t.Date)),
            budgetSummary);

        return new AnalysisReport(
            selected,
            selected == latest,
            comparedThroughDay,
            months,
            summary,
            previous,
            categoryTotals,
            trend,
            recurring,
            income,
            forecast,
            budgetSummary,
            feesYearToDate,
            insights);
    }
}
