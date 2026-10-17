using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public sealed record CategoryTotal(
    string CategoryId,
    string Name,
    string Colour,
    string Icon,
    bool IsEssential,
    decimal Amount,
    decimal PreviousAmount,
    int Count)
{
    public decimal Change => Amount - PreviousAmount;
    public double? ChangePercent => PreviousAmount == 0 ? null : (double)(Change / PreviousAmount);
}

public sealed record MonthSummary(
    YearMonth Month,
    decimal Spent,
    decimal Income,
    decimal Essential,
    decimal NonEssential,
    decimal Uncategorised,
    decimal BankFees,
    int TransactionCount)
{
    public static MonthSummary Empty(YearMonth month) => new(month, 0, 0, 0, 0, 0, 0, 0);
}

public enum RecurringCadence { Weekly, Fortnightly, Monthly, Quarterly, Yearly }

public enum RecurringKind { Subscription, DebitOrder, Regular }

public sealed record RecurringCharge(
    string Merchant,
    string CategoryId,
    RecurringKind Kind,
    RecurringCadence Cadence,
    decimal Amount,
    decimal MonthlyCost,
    DateOnly FirstDate,
    DateOnly LastDate,
    DateOnly NextDate,
    int Occurrences,
    IReadOnlyList<Guid> TransactionIds);

// A regular credit such as a salary. TypicalDay is the day of the month it normally lands on;
// WeekendRule is what the history shows happens when that day is a weekend (null if never seen).
public sealed record IncomeSource(
    string Name,
    decimal Amount,
    RecurringCadence Cadence,
    int TypicalDay,
    DateOnly LastDate,
    DateOnly NextDate,
    int Occurrences,
    WeekendPayday? WeekendRule,
    IReadOnlyList<Guid> TransactionIds);

// Everyday spending, learned from recent history with one-off big purchases set aside.
public sealed record SpendModel(
    IReadOnlyList<decimal> ByWeekday,
    decimal AverageDaily,
    decimal DailyStdDev,
    int WindowDays,
    int OneOffCount,
    decimal OneOffTotal)
{
    public decimal For(DayOfWeek day) => ByWeekday[(int)day];
    public decimal WeekdayAverage => Enumerable.Range(1, 5).Average(i => ByWeekday[i]);
    public decimal WeekendAverage => (ByWeekday[0] + ByWeekday[6]) / 2;
}

public sealed record ForecastPoint(DateOnly Date, decimal Balance, decimal Low, decimal High);

public sealed record UpcomingDebit(RecurringCharge Charge, DateOnly Date, decimal BalanceBefore)
{
    public bool AtRisk => BalanceBefore < Charge.Amount;
}

public sealed record UpcomingIncome(IncomeSource Source, DateOnly Date);

public enum PayDateSource { Settings, DetectedSalary }

public sealed record PaydayForecast(
    DateOnly AsOf,
    DateOnly PayDate,
    PayDateSource PayDateSource,
    IncomeSource? Salary,
    decimal StartBalance,
    decimal EndBalance,
    decimal EndLow,
    decimal EndHigh,
    decimal LowestBalance,
    SpendModel Spend,
    IReadOnlyList<ForecastPoint> Points,
    IReadOnlyList<UpcomingDebit> UpcomingDebits,
    IReadOnlyList<UpcomingIncome> UpcomingIncome,
    DateOnly? RunsOutOn)
{
    public int DaysToPayday => PayDate.DayNumber - AsOf.DayNumber;
    public decimal AverageDailySpend => Spend.AverageDaily;
}

public enum BudgetState { OnTrack, Watch, Over }

public sealed record BudgetStatus(
    string CategoryId,
    string Name,
    string Colour,
    string Icon,
    decimal Limit,
    decimal Spent,
    decimal Projected,
    BudgetState State)
{
    public decimal Remaining => Limit - Spent;
    public double UsedFraction => Limit <= 0 ? 0 : (double)(Spent / Limit);
    public double ProjectedFraction => Limit <= 0 ? 0 : (double)(Projected / Limit);
}

public sealed record BudgetSummary(
    IReadOnlyList<BudgetStatus> Items,
    decimal TotalLimit,
    decimal TotalSpent,
    decimal TotalProjected,
    int DaysLeft,
    bool InProgress)
{
    public int OverCount => Items.Count(i => i.State == BudgetState.Over);
    public int WatchCount => Items.Count(i => i.State == BudgetState.Watch);
}

public sealed record BudgetSuggestion(string CategoryId, string Name, string Colour, string Icon, decimal Amount, decimal MonthlyAverage, int Months);

public enum InsightKind { Positive, Neutral, Warning }

// CategoryId, when set, is the category the insight is about, so the UI can link to its transactions.
public sealed record Insight(string Id, InsightKind Kind, string Icon, string Text, int Priority, string? CategoryId = null);

public sealed record AnalysisReport(
    YearMonth Month,
    bool IsLatestMonth,
    int? ComparedThroughDay,
    IReadOnlyList<YearMonth> AvailableMonths,
    MonthSummary Summary,
    MonthSummary? Previous,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<MonthSummary> Trend,
    IReadOnlyList<RecurringCharge> Recurring,
    IReadOnlyList<IncomeSource> Income,
    PaydayForecast? Forecast,
    BudgetSummary? Budgets,
    decimal BankFeesYearToDate,
    IReadOnlyList<Insight> Insights)
{
    public decimal RecurringMonthlyTotal => Recurring.Sum(r => r.MonthlyCost);
}
