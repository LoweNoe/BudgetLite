using System.Globalization;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public sealed class InsightGenerator
{
    private const decimal HighBankFees = 200m;

    private readonly string _currency;

    public InsightGenerator(string currency)
    {
        _currency = currency;
    }

    public IReadOnlyList<Insight> Generate(
        YearMonth month,
        bool isLatestMonth,
        bool monthInProgress,
        MonthSummary summary,
        MonthSummary? previous,
        IReadOnlyList<CategoryTotal> categories,
        IReadOnlyList<RecurringCharge> recurring,
        PaydayForecast? forecast,
        decimal bankFeesYearToDate,
        IEnumerable<Transaction> monthTransactions,
        BudgetSummary? budgets = null)
    {
        var insights = new List<Insight>();
        var when = isLatestMonth ? "this month" : $"in {month}";
        var lastMonth = monthInProgress ? "this time last month" : "last month";

        if (isLatestMonth && forecast is not null)
            AddForecast(insights, forecast);

        if (budgets is { Items.Count: > 0 })
            AddBudgets(insights, budgets, isLatestMonth, month);

        AddCategoryChange(insights, categories, previous is not null, when, lastMonth);
        AddTotalChange(insights, summary, previous, lastMonth);

        if (summary.Spent > 0 && summary.NonEssential > 0)
        {
            var share = summary.NonEssential / summary.Spent;
            insights.Add(new Insight("non-essential", share >= 0.4m ? InsightKind.Warning : InsightKind.Neutral, "sparkles",
                $"{Money(summary.NonEssential)} of your spending {when} ({Percent(share)}) went on non-essentials.", 70));
        }

        var subscriptions = recurring.Where(r => r.Kind == RecurringKind.Subscription).ToList();
        if (subscriptions.Count > 0)
        {
            var monthly = subscriptions.Sum(s => s.MonthlyCost);
            insights.Add(new Insight("subscriptions", InsightKind.Neutral, "repeat",
                $"Your {Count(subscriptions.Count, "subscription")} cost {Money(monthly)} a month, or {Money(monthly * 12)} a year.", 60, CategoryIds.Subscriptions));
        }

        var fresh = recurring.Where(r => r.Occurrences == 2 && month.Contains(r.LastDate) && r.Kind != RecurringKind.Regular).ToList();
        foreach (var charge in fresh.Take(2))
        {
            var label = charge.Kind == RecurringKind.Subscription ? "subscription" : "debit order";
            insights.Add(new Insight($"new-{charge.Merchant}", InsightKind.Neutral, "sparkles",
                $"New {label} spotted: {charge.Merchant}, {Money(charge.MonthlyCost)} a month.", 55));
        }

        if (summary.BankFees > 0)
        {
            insights.Add(new Insight("bank-fees", summary.BankFees >= HighBankFees ? InsightKind.Warning : InsightKind.Neutral, "landmark",
                $"You paid {Money(summary.BankFees)} in bank fees {when} ({Money(bankFeesYearToDate)} so far in {month.Year}).", 50, CategoryIds.BankFees));
        }

        var uncategorised = monthTransactions.Where(t => t.CategoryId == CategoryIds.Uncategorised && t.Amount < 0).ToList();
        if (uncategorised.Count > 0)
        {
            insights.Add(new Insight("uncategorised", InsightKind.Neutral, "help",
                $"{Money(-uncategorised.Sum(t => t.Amount))} across {Count(uncategorised.Count, "transaction")} isn't categorised yet. Sorting it makes these insights sharper.", 40, CategoryIds.Uncategorised));
        }

        var recurringIds = recurring.SelectMany(r => r.TransactionIds).ToHashSet();
        var biggest = monthTransactions
            .Where(t => t.Amount < 0 && SpendingAnalyzer.CountsAsSpending(t) && !recurringIds.Contains(t.Id))
            .MinBy(t => t.Amount);
        if (biggest is not null)
        {
            var name = TextCase.Title(string.IsNullOrWhiteSpace(biggest.CleanDescription) ? biggest.Description : biggest.CleanDescription);
            insights.Add(new Insight("biggest", InsightKind.Neutral, "bag",
                $"Your biggest purchase {when} was {Money(-biggest.Amount)} at {name}.", 30));
        }

        return insights.OrderByDescending(i => i.Priority).ToList();
    }

    private void AddForecast(List<Insight> insights, PaydayForecast forecast)
    {
        var payday = forecast.PayDateSource == PayDateSource.DetectedSalary
            ? $"your salary arrives on {Weekday(forecast.PayDate)}"
            : $"you get paid on {Weekday(forecast.PayDate)}";

        if (forecast.RunsOutOn is { } runsOut)
        {
            var daysEarly = forecast.PayDate.DayNumber - runsOut.DayNumber;
            insights.Add(new Insight("forecast", InsightKind.Warning, "alert",
                $"At your current pace, your balance could run out around {Day(runsOut)}, {Count(daysEarly, "day")} before {(forecast.PayDateSource == PayDateSource.DetectedSalary ? "your salary arrives" : "payday")}.", 100));
        }
        else if (forecast.EndLow < 0)
        {
            insights.Add(new Insight("forecast", InsightKind.Warning, "alert",
                $"You should have about {Money(forecast.EndBalance)} left when {payday}, but a busy few days could take you into the red. Go gently.", 90));
        }
        else
        {
            insights.Add(new Insight("forecast", InsightKind.Positive, "calendar",
                $"You're on track to have about {Money(forecast.EndBalance)} left when {payday} (likely {Money(forecast.EndLow)} to {Money(forecast.EndHigh)}).", 85));
        }

        foreach (var debit in forecast.UpcomingDebits.Where(d => d.AtRisk).Take(2))
        {
            var label = debit.Charge.Kind == RecurringKind.DebitOrder ? "debit order" : "payment";
            insights.Add(new Insight($"risk-{debit.Charge.Merchant}", InsightKind.Warning, "alert",
                $"Your {debit.Charge.Merchant} {label} ({Money(debit.Charge.Amount)}) on {Day(debit.Date)} might bounce. Your balance could be down to {Money(debit.BalanceBefore)} by then.", 95));
        }
    }

    private void AddBudgets(List<Insight> insights, BudgetSummary budgets, bool isLatestMonth, YearMonth month)
    {
        if (!isLatestMonth)
        {
            var within = budgets.Items.Count(b => b.State != BudgetState.Over);
            insights.Add(new Insight("budgets", within == budgets.Items.Count ? InsightKind.Positive : InsightKind.Neutral, "target",
                within == budgets.Items.Count
                    ? $"You stayed within all {Count(budgets.Items.Count, "budget")} in {month}."
                    : $"You stayed within {within} of {Count(budgets.Items.Count, "budget")} in {month}.", 75));
            return;
        }

        foreach (var over in budgets.Items.Where(b => b.State == BudgetState.Over).Take(2))
        {
            insights.Add(new Insight($"budget-over-{over.CategoryId}", InsightKind.Warning, "target",
                $"You're {Money(over.Spent - over.Limit)} over your {over.Name.ToLowerInvariant()} budget this month.", 92, over.CategoryId));
        }

        foreach (var watch in budgets.Items.Where(b => b.State == BudgetState.Watch).Take(2))
        {
            var text = watch.Projected > watch.Limit && budgets.InProgress
                ? $"At this pace, {watch.Name.ToLowerInvariant()} will go about {Money(watch.Projected - watch.Limit)} over budget by month end."
                : $"You've used {Math.Round(watch.UsedFraction * 100):0}% of your {watch.Name.ToLowerInvariant()} budget with {Count(budgets.DaysLeft, "day")} to go.";
            insights.Add(new Insight($"budget-watch-{watch.CategoryId}", InsightKind.Warning, "target", text, 88, watch.CategoryId));
        }

        if (budgets.OverCount == 0 && budgets.WatchCount == 0)
        {
            insights.Add(new Insight("budgets", InsightKind.Positive, "target",
                budgets.Items.Count == 1
                    ? "Your budget is on track this month."
                    : $"All {Count(budgets.Items.Count, "budget")} are on track this month.", 78));
        }
    }

    private void AddCategoryChange(List<Insight> insights, IReadOnlyList<CategoryTotal> categories, bool hasPrevious, string when, string lastMonth)
    {
        var top = categories
            .Where(c => !c.IsEssential && c.CategoryId != CategoryIds.Uncategorised && c.Amount > 0)
            .MaxBy(c => c.Amount);
        if (top is null)
            return;

        var name = top.Name.ToLowerInvariant();
        var lead = $"You spent {Money(top.Amount)} on {name} {when}";

        if (!hasPrevious || top.PreviousAmount <= 0)
        {
            insights.Add(new Insight("top-category", InsightKind.Neutral, top.Icon, $"{lead}.", 80, top.CategoryId));
            return;
        }

        var change = top.Change;
        var (kind, text) = change switch
        {
            > 0 => (top.ChangePercent >= 0.25 ? InsightKind.Warning : InsightKind.Neutral, $"{lead}, {Money(change)} more than {lastMonth}."),
            < 0 => (InsightKind.Positive, $"{lead}, {Money(-change)} less than {lastMonth}."),
            _ => (InsightKind.Neutral, $"{lead}, the same as {lastMonth}."),
        };
        insights.Add(new Insight("top-category", kind, top.Icon, text, 80, top.CategoryId));
    }

    private void AddTotalChange(List<Insight> insights, MonthSummary summary, MonthSummary? previous, string lastMonth)
    {
        if (previous is null || previous.Spent <= 0 || summary.Spent <= 0)
            return;

        var change = summary.Spent - previous.Spent;
        var percent = Math.Abs(change / previous.Spent);
        if (percent < 0.03m)
        {
            insights.Add(new Insight("total-change", InsightKind.Neutral, "arrows",
                $"Your spending is about the same as {lastMonth}.", 65));
        }
        else if (change < 0)
        {
            insights.Add(new Insight("total-change", InsightKind.Positive, "check-circle",
                $"You've spent {Percent(percent)} less than {lastMonth} ({Money(-change)}). Nice work.", 65));
        }
        else
        {
            insights.Add(new Insight("total-change", percent >= 0.15m ? InsightKind.Warning : InsightKind.Neutral, "arrows",
                $"You've spent {Money(change)} ({Percent(percent)}) more than {lastMonth}.", 65));
        }
    }

    private string Money(decimal amount) => MoneyFormatter.Format(amount, _currency, wholeUnits: true);

    private static string Percent(decimal fraction) => $"{Math.Round(fraction * 100):0}%";

    private static string Day(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);

    private static string Weekday(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
