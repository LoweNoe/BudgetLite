using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using static BudgetApp.Core.Models.CategoryIds;
using static BudgetApp.Tests.Core.AnalysisTestData;

namespace BudgetApp.Tests.Core;

public class BudgetTrackerTests
{
    private static readonly YearMonth October = new(2026, 10);

    private static BudgetSummary Evaluate(List<Transaction> transactions, params (string Category, decimal Amount)[] budgets)
    {
        var asOf = transactions.Max(t => t.Date);
        var recurring = RecurringDetector.Detect(transactions, asOf);
        return BudgetTracker.Evaluate(
            budgets.Select(b => new CategoryBudget { CategoryId = b.Category, Amount = b.Amount }).ToList(),
            transactions, Categories(), recurring, YearMonth.From(asOf), asOf, isLatestMonth: true);
    }

    [Fact]
    public void Tracks_spent_remaining_and_state()
    {
        var summary = Evaluate(
        [
            T("2026-10-02", -400, "UBER EATS", Takeaways),
            T("2026-10-31", -100, "SPAR", Groceries),
        ], (Takeaways, 1000), (Groceries, 2000));

        var takeaways = summary.Items.Single(i => i.CategoryId == Takeaways);
        Assert.Equal(400m, takeaways.Spent);
        Assert.Equal(600m, takeaways.Remaining);
        Assert.Equal(BudgetState.OnTrack, takeaways.State);
        Assert.False(summary.InProgress);
        Assert.Equal(3000m, summary.TotalLimit);
    }

    [Fact]
    public void Projects_month_end_from_everyday_pace()
    {
        // R 500 in the first 10 days of a 31-day month projects to R 1 550.
        var summary = Evaluate([T("2026-10-05", -500, "UBER EATS", Takeaways), T("2026-10-10", -1, "SPAR", Groceries)], (Takeaways, 1200));

        var takeaways = Assert.Single(summary.Items);
        Assert.True(summary.InProgress);
        Assert.Equal(21, summary.DaysLeft);
        Assert.Equal(1550m, takeaways.Projected);
        Assert.Equal(BudgetState.Watch, takeaways.State);
    }

    [Fact]
    public void Lumpy_categories_use_their_usual_month_when_there_is_history()
    {
        // Fuel: two fill-ups a month. After the first one in October the pace looks alarming,
        // but the usual R 1 600 a month says there's one more fill-up to come.
        var summary = Evaluate(
        [
            T("2026-08-08", -760, "ENGEN", Fuel), T("2026-08-22", -840, "SHELL", Fuel),
            T("2026-09-08", -880, "ENGEN", Fuel), T("2026-09-22", -720, "SHELL", Fuel),
            T("2026-10-08", -800, "ENGEN", Fuel),
            T("2026-10-14", -1, "SPAR", Groceries),
        ], (Fuel, 1700));

        var fuel = Assert.Single(summary.Items);
        Assert.Equal(800m + decimal.Round(1600m * 17 / 31, 2), fuel.Projected);
        Assert.Equal(BudgetState.OnTrack, fuel.State);
    }

    [Fact]
    public void Recurring_payments_still_due_count_towards_the_projection()
    {
        var summary = Evaluate(
        [
            T("2026-09-20", -199, "NETFLIX.COM", Subscriptions),
            T("2026-08-20", -199, "NETFLIX.COM", Subscriptions),
            T("2026-10-10", -50, "SPOTIFY P1", Subscriptions),
        ], (Subscriptions, 300));

        var subs = Assert.Single(summary.Items);
        // Spotify's R 50 pace over 21 more days plus Netflix's R 199 on the 20th.
        Assert.Equal(50m + 105m + 199m, subs.Projected);
        Assert.Equal(BudgetState.Watch, subs.State);
    }

    [Fact]
    public void Nearly_spent_but_nothing_more_expected_stays_on_track()
    {
        var summary = Evaluate(
        [
            T("2026-08-05", -199, "NETFLIX.COM", Subscriptions),
            T("2026-09-05", -199, "NETFLIX.COM", Subscriptions),
            T("2026-10-05", -199, "NETFLIX.COM", Subscriptions),
            T("2026-10-14", -1, "SPAR", Groceries),
        ], (Subscriptions, 210));

        var subs = Assert.Single(summary.Items);
        Assert.Equal(199m, subs.Projected);
        Assert.Equal(BudgetState.OnTrack, subs.State);
    }

    [Fact]
    public void Overspending_is_flagged_first()
    {
        var summary = Evaluate(
        [
            T("2026-10-02", -1300, "TAKEALOT", Shopping),
            T("2026-10-03", -10, "SPAR", Groceries),
            T("2026-10-31", -1, "SPAR", Groceries),
        ], (Groceries, 3000), (Shopping, 1000));

        Assert.Equal(BudgetState.Over, summary.Items[0].State);
        Assert.Equal(Shopping, summary.Items[0].CategoryId);
        Assert.Equal(1, summary.OverCount);
    }

    [Fact]
    public void Suggestions_average_complete_months_and_round_up()
    {
        var transactions = new List<Transaction>
        {
            T("2026-07-05", -1180, "PNP", Groceries),
            T("2026-08-05", -1320, "PNP", Groceries),
            T("2026-09-05", -1240, "PNP", Groceries),
            T("2026-09-06", -330, "UBER EATS", Takeaways),
            T("2026-10-05", -5000, "PNP", Groceries),
            T("2026-09-10", -20, "SPOTIFY", Subscriptions),
        };

        var suggestions = BudgetTracker.Suggest(transactions, Categories(), [new CategoryBudget { CategoryId = Subscriptions, Amount = 100 }]);

        var groceries = suggestions.Single(s => s.CategoryId == Groceries);
        Assert.Equal(1246.67m, decimal.Round(groceries.MonthlyAverage, 2));
        Assert.Equal(1300m, groceries.Amount);
        Assert.Equal(3, groceries.Months);
        Assert.Equal(150m, suggestions.Single(s => s.CategoryId == Takeaways).Amount);
        Assert.DoesNotContain(suggestions, s => s.CategoryId == Subscriptions);
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(999, 1000)]
    [InlineData(1001, 1100)]
    [InlineData(5001, 5500)]
    public void Rounds_suggestions_to_tidy_steps(double amount, double expected)
    {
        Assert.Equal((decimal)expected, BudgetTracker.RoundUp((decimal)amount));
    }

    [Fact]
    public void Budget_insights_speak_plainly()
    {
        var transactions = new List<Transaction>
        {
            T("2026-10-02", -1300, "TAKEALOT", Shopping),
            T("2026-10-05", -500, "UBER EATS", Takeaways),
            T("2026-10-10", -1, "SPAR", Groceries),
        };
        var budgets = new List<CategoryBudget>
        {
            new() { CategoryId = Shopping, Amount = 1000 },
            new() { CategoryId = Takeaways, Amount = 1200 },
        };

        var report = BudgetAnalyzer.Analyse(transactions, Categories(), new AppSettings(), budgets: budgets)!;
        var texts = report.Insights.Select(i => i.Text.Replace(MoneyFormatter.Space, ' ')).ToList();

        Assert.Contains("You're R 300 over your shopping budget this month.", texts);
        Assert.Contains("At this pace, takeaways will go about R 350 over budget by month end.", texts);
    }

    [Fact]
    public void All_on_track_gets_a_positive_note()
    {
        var report = BudgetAnalyzer.Analyse(
            [T("2026-10-02", -100, "UBER EATS", Takeaways), T("2026-10-31", -1, "SPAR", Groceries)],
            Categories(), new AppSettings(),
            budgets: [new CategoryBudget { CategoryId = Takeaways, Amount = 1000 }, new CategoryBudget { CategoryId = Groceries, Amount = 3000 }])!;

        var insight = report.Insights.Single(i => i.Id == "budgets");
        Assert.Equal(InsightKind.Positive, insight.Kind);
        Assert.Equal("All 2 budgets are on track this month.", insight.Text);
    }
}
