using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using static BudgetApp.Core.Models.CategoryIds;
using static BudgetApp.Tests.Core.AnalysisTestData;

namespace BudgetApp.Tests.Core;

public class ForecastAndInsightTests
{
    private static PaydayForecast Forecast(List<Transaction> transactions, int payDay = 25, WeekendPayday rule = WeekendPayday.SameDay)
    {
        var asOf = transactions.Max(t => t.Date);
        var settings = new AppSettings { PayDay = payDay, WeekendPayday = rule };
        var recurring = RecurringDetector.Detect(transactions, asOf);
        var income = IncomeDetector.Detect(transactions, asOf, rule);
        return ForecastCalculator.Calculate(transactions, recurring, income, settings)!;
    }

    [Theory]
    [InlineData("2025-03-10", 25, "2025-03-25")]
    [InlineData("2025-03-25", 25, "2025-04-25")]
    [InlineData("2025-03-28", 25, "2025-04-25")]
    [InlineData("2025-02-10", 30, "2025-02-28")]
    [InlineData("2025-01-31", 31, "2025-02-28")]
    public void Next_payday_rolls_over_and_clamps_short_months(string asOf, int payDay, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), ForecastCalculator.NextPayday(DateOnly.Parse(asOf), payDay));
    }

    [Theory]
    [InlineData("2026-10-25", WeekendPayday.FridayBefore, "2026-10-23")]
    [InlineData("2026-10-25", WeekendPayday.MondayAfter, "2026-10-26")]
    [InlineData("2026-10-25", WeekendPayday.SameDay, "2026-10-25")]
    [InlineData("2026-10-24", WeekendPayday.FridayBefore, "2026-10-23")]
    [InlineData("2026-10-21", WeekendPayday.FridayBefore, "2026-10-21")]
    public void Weekend_paydays_move_according_to_the_rule(string date, WeekendPayday rule, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), IncomeDetector.AdjustForWeekend(DateOnly.Parse(date), rule));
    }

    [Fact]
    public void Pay_date_setting_skips_a_weekend_payday_that_already_passed()
    {
        // 25 Oct 2026 is a Sunday, so with "Friday before" payday is Fri 23 Oct.
        Assert.Equal(new DateOnly(2026, 10, 23), IncomeDetector.NextPayDate(new DateOnly(2026, 10, 14), 25, WeekendPayday.FridayBefore));
        Assert.Equal(new DateOnly(2026, 11, 25), IncomeDetector.NextPayDate(new DateOnly(2026, 10, 23), 25, WeekendPayday.FridayBefore));
    }

    [Fact]
    public void Detects_salary_and_learns_the_weekend_rule()
    {
        var transactions = new List<Transaction>
        {
            T("2026-05-25", 30000, "SALARY ACME", Income),   // Monday
            T("2026-06-25", 30000, "SALARY ACME", Income),   // Thursday
            T("2026-07-24", 30000, "SALARY ACME", Income),   // 25th was a Saturday, paid Friday
            T("2026-08-25", 30000, "SALARY ACME", Income),
            T("2026-09-25", 30500, "SALARY ACME", Income),
            T("2026-10-14", -100, "SPAR", Groceries, 5000),
        };

        var income = IncomeDetector.Detect(transactions, new DateOnly(2026, 10, 14), WeekendPayday.SameDay);
        var salary = IncomeDetector.Salary(income)!;

        Assert.Equal(25, salary.TypicalDay);
        Assert.Equal(WeekendPayday.FridayBefore, salary.WeekendRule);
        Assert.Equal(new DateOnly(2026, 10, 23), salary.NextDate);
        Assert.Equal(30000m, salary.Amount);
    }

    [Fact]
    public void Forecast_uses_the_detected_salary_date_over_the_setting()
    {
        var transactions = new List<Transaction>
        {
            T("2026-08-28", 20000, "SALARY ACME", Income),
            T("2026-09-28", 20000, "SALARY ACME", Income),
            T("2026-10-10", -100, "SPAR", Groceries, 5000),
        };

        var forecast = Forecast(transactions, payDay: 15);

        Assert.Equal(PayDateSource.DetectedSalary, forecast.PayDateSource);
        Assert.Equal(new DateOnly(2026, 10, 28), forecast.PayDate);
    }

    [Fact]
    public void Other_regular_income_before_payday_is_added()
    {
        var transactions = new List<Transaction>
        {
            T("2026-08-25", 20000, "SALARY ACME", Income),
            T("2026-09-25", 20000, "SALARY ACME", Income),
            T("2026-08-18", 1500, "TUTORING J SMITH", Income),
            T("2026-09-18", 1500, "TUTORING J SMITH", Income),
            T("2026-10-10", -10, "SPAR", Groceries, 1000),
        };

        var forecast = Forecast(transactions);

        var credit = Assert.Single(forecast.UpcomingIncome);
        Assert.Equal(new DateOnly(2026, 10, 18), credit.Date);
        Assert.True(forecast.EndBalance > 2000);
    }

    [Fact]
    public void Spending_model_learns_weekday_patterns()
    {
        var transactions = new List<Transaction>();
        for (var d = new DateOnly(2026, 7, 6); d <= new DateOnly(2026, 9, 27); d = d.AddDays(1))
        {
            var amount = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? -600 : -100;
            transactions.Add(T(d.ToString("yyyy-MM-dd"), amount, "SHOP", Groceries));
        }

        var model = ForecastCalculator.BuildSpendModel(transactions, new HashSet<Guid>(), new DateOnly(2026, 9, 27));

        Assert.Equal(600m, model.For(DayOfWeek.Saturday));
        Assert.Equal(100m, model.For(DayOfWeek.Tuesday));
        Assert.Equal(100m, model.WeekdayAverage);
        Assert.Equal(600m, model.WeekendAverage);
    }

    [Fact]
    public void One_off_purchases_are_set_aside()
    {
        var transactions = new List<Transaction>();
        for (var d = new DateOnly(2026, 9, 1); d <= new DateOnly(2026, 9, 28); d = d.AddDays(1))
            transactions.Add(T(d.ToString("yyyy-MM-dd"), -100, "SHOP", Groceries));
        transactions.Add(T("2026-09-15", -14000, "HIFI CORP TV", Shopping));

        var model = ForecastCalculator.BuildSpendModel(transactions, new HashSet<Guid>(), new DateOnly(2026, 9, 28));

        Assert.Equal(1, model.OneOffCount);
        Assert.Equal(14000m, model.OneOffTotal);
        Assert.Equal(100m, model.AverageDaily);
    }

    [Fact]
    public void Likely_range_widens_further_out()
    {
        var transactions = new List<Transaction>();
        var rng = new Random(7);
        for (var d = new DateOnly(2026, 8, 1); d <= new DateOnly(2026, 10, 10); d = d.AddDays(1))
            transactions.Add(T(d.ToString("yyyy-MM-dd"), -rng.Next(50, 400), "SHOP", Groceries));
        transactions[^1].Balance = 20000;

        var forecast = Forecast(transactions);
        var first = forecast.Points[1];
        var last = forecast.Points[^1];

        Assert.Equal(forecast.Points[0].Balance, forecast.Points[0].Low);
        Assert.True(last.High - last.Low > first.High - first.Low);
        Assert.True(forecast.EndLow < forecast.EndBalance && forecast.EndBalance < forecast.EndHigh);
    }

    [Fact]
    public void Projects_balance_with_spending_and_upcoming_debits()
    {
        var transactions = new List<Transaction>
        {
            T("2025-02-12", -199, "NETFLIX.COM", Subscriptions, 5000),
            T("2025-03-01", -100, "SPAR", Groceries, 4900),
            T("2025-03-12", -199, "NETFLIX.COM", Subscriptions, 4701),
            T("2025-03-15", -100, "SPAR", Groceries, 4601),
        };

        var forecast = Forecast(transactions);

        Assert.Equal(new DateOnly(2025, 3, 15), forecast.AsOf);
        Assert.Equal(new DateOnly(2025, 3, 25), forecast.PayDate);
        Assert.Equal(PayDateSource.Settings, forecast.PayDateSource);
        Assert.Equal(4601m, forecast.StartBalance);
        Assert.Equal(11, forecast.Points.Count);
        // Both shops were on Saturdays: 200 over the five Saturdays in the window, and one more Saturday before payday.
        Assert.Equal(40m, forecast.Spend.For(DayOfWeek.Saturday));
        Assert.Equal(4561m, forecast.EndBalance);
        Assert.Empty(forecast.UpcomingDebits);
        Assert.Null(forecast.RunsOutOn);
    }

    [Fact]
    public void Upcoming_debit_before_payday_is_marked_at_risk()
    {
        var transactions = new List<Transaction>
        {
            T("2025-01-22", -899, "DSTV", Utilities, raw: "DEBIT ORDER DSTV"),
            T("2025-02-22", -899, "DSTV", Utilities, 3000, raw: "DEBIT ORDER DSTV"),
            T("2025-03-15", -100, "SPAR", Groceries, 600),
        };

        var forecast = Forecast(transactions);

        var debit = Assert.Single(forecast.UpcomingDebits);
        Assert.Equal(new DateOnly(2025, 3, 22), debit.Date);
        Assert.True(debit.AtRisk);
        Assert.NotNull(forecast.RunsOutOn);
    }

    [Fact]
    public void No_balance_column_means_no_forecast()
    {
        Assert.Null(ForecastCalculator.Calculate([T("2025-03-01", -100, "SPAR", Groceries)], [], [], new AppSettings()));
    }

    [Fact]
    public void A_month_in_progress_is_compared_with_the_same_days_last_month()
    {
        var transactions = new List<Transaction>
        {
            T("2025-02-03", -300, "UBER EATS", Takeaways),
            T("2025-02-20", -900, "MR D FOOD", Takeaways),
            T("2025-03-04", -500, "UBER EATS", Takeaways),
        };

        var report = BudgetAnalyzer.Analyse(transactions, Categories(), new AppSettings())!;

        Assert.Equal(4, report.ComparedThroughDay);
        Assert.Equal(300m, report.Previous!.Spent);
        Assert.Equal("You spent R 500 on takeaways this month, R 200 more than this time last month.",
            Plain(report.Insights.Single(i => i.Id == "top-category").Text));
    }

    [Fact]
    public void Writes_plain_language_category_comparisons()
    {
        var transactions = new List<Transaction>
        {
            T("2025-02-03", -930, "UBER EATS", Takeaways),
            T("2025-03-03", -1240, "UBER EATS", Takeaways),
            T("2025-03-31", -50, "SPAR", Groceries),
        };

        var report = BudgetAnalyzer.Analyse(transactions, Categories(), new AppSettings())!;
        var text = Plain(report.Insights.Single(i => i.Id == "top-category").Text);

        Assert.Equal("You spent R 1 240 on takeaways this month, R 310 more than last month.", text);
        Assert.Equal(InsightKind.Warning, report.Insights.Single(i => i.Id == "top-category").Kind);
    }

    [Fact]
    public void Older_months_are_described_by_name()
    {
        var transactions = new List<Transaction>
        {
            T("2025-02-03", -930, "UBER EATS", Takeaways),
            T("2025-03-03", -1240, "UBER EATS", Takeaways),
        };

        var report = BudgetAnalyzer.Analyse(transactions, Categories(), new AppSettings(), new YearMonth(2025, 2))!;

        Assert.False(report.IsLatestMonth);
        Assert.StartsWith("You spent R 930 on takeaways in February 2025", Plain(report.Insights.Single(i => i.Id == "top-category").Text));
    }

    [Fact]
    public void Report_puts_warnings_first_and_summarises_subscriptions()
    {
        var transactions = new List<Transaction>
        {
            T("2025-02-05", -199, "NETFLIX.COM", Subscriptions, 900),
            T("2025-02-14", -60, "SPOTIFY", Subscriptions, 840),
            T("2025-03-05", -199, "NETFLIX.COM", Subscriptions, 641),
            T("2025-03-06", -600, "TAKEALOT", Shopping, 41),
            T("2025-03-14", -60, "SPOTIFY", Subscriptions, -19),
        };

        var report = BudgetAnalyzer.Analyse(transactions, Categories(), new AppSettings { PayDay = 25 })!;

        Assert.Equal(InsightKind.Warning, report.Insights[0].Kind);
        Assert.Contains(report.Insights, i => Plain(i.Text) == "Your 2 subscriptions cost R 259 a month, or R 3 108 a year.");
        Assert.Equal(259m, report.RecurringMonthlyTotal);
        Assert.Equal([new YearMonth(2025, 2), new YearMonth(2025, 3)], report.AvailableMonths);
        Assert.Equal(BudgetAnalyzer.TrendMonths, report.Trend.Count);
    }

    [Fact]
    public void Nothing_to_analyse_without_transactions()
    {
        Assert.Null(BudgetAnalyzer.Analyse([], Categories(), new AppSettings()));
    }

    private static string Plain(string text) => text.Replace(MoneyFormatter.Space, ' ');
}
