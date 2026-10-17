using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using static BudgetApp.Core.Models.CategoryIds;
using static BudgetApp.Tests.Core.AnalysisTestData;

namespace BudgetApp.Tests.Core;

public class SpendingAnalyzerTests
{
    private static readonly YearMonth March = new(2025, 3);
    private readonly SpendingAnalyzer _analyzer = new(Categories());

    [Fact]
    public void Year_month_arithmetic_wraps_years()
    {
        Assert.Equal(new YearMonth(2024, 12), new YearMonth(2025, 1).AddMonths(-1));
        Assert.Equal(new DateOnly(2024, 2, 29), new YearMonth(2024, 2).Last);
        Assert.True(new YearMonth(2024, 12) < new YearMonth(2025, 1));
        Assert.Equal("March 2025", March.ToString());
    }

    [Fact]
    public void Summary_splits_essential_non_essential_and_ignores_transfers()
    {
        var transactions = new[]
        {
            T("2025-03-02", -1000, "PNP", Groceries),
            T("2025-03-03", -300, "UBER EATS", Takeaways),
            T("2025-03-04", -200, "MYSTERY", Uncategorised),
            T("2025-03-05", -69, "MONTHLY FEE", BankFees),
            T("2025-03-06", -5000, "SAVINGS", Transfers),
            T("2025-03-25", 25000, "SALARY", Income),
            T("2025-02-10", -999, "LAST MONTH", Groceries),
        };

        var summary = _analyzer.Summarise(transactions, March);

        Assert.Equal(1569m, summary.Spent);
        Assert.Equal(25000m, summary.Income);
        Assert.Equal(1069m, summary.Essential);
        Assert.Equal(300m, summary.NonEssential);
        Assert.Equal(200m, summary.Uncategorised);
        Assert.Equal(69m, summary.BankFees);
        Assert.Equal(6, summary.TransactionCount);
    }

    [Fact]
    public void Refunds_net_off_against_their_category()
    {
        var transactions = new[]
        {
            T("2025-03-02", -1650, "TAKEALOT", Shopping),
            T("2025-03-09", 350, "TAKEALOT REFUND", Shopping),
        };

        var total = Assert.Single(_analyzer.CategoryTotals(transactions, March));
        Assert.Equal(1300m, total.Amount);
    }

    [Fact]
    public void Category_totals_include_month_over_month_change()
    {
        var transactions = new[]
        {
            T("2025-02-03", -930, "UBER EATS", Takeaways),
            T("2025-03-03", -1240, "UBER EATS", Takeaways),
            T("2025-02-10", -400, "STER KINEKOR", Entertainment),
        };

        var totals = _analyzer.CategoryTotals(transactions, March);
        var takeaways = totals.Single(t => t.CategoryId == Takeaways);
        var entertainment = totals.Single(t => t.CategoryId == Entertainment);

        Assert.Equal(310m, takeaways.Change);
        Assert.Equal(1240m / 930m - 1, (decimal)takeaways.ChangePercent!.Value, 3);
        Assert.Equal(0m, entertainment.Amount);
        Assert.Equal(400m, entertainment.PreviousAmount);
    }

    [Fact]
    public void Trend_returns_one_summary_per_month_including_empty_ones()
    {
        var trend = _analyzer.Trend([T("2025-01-05", -100, "SPAR", Groceries), T("2025-03-05", -200, "SPAR", Groceries)], March, 3);

        Assert.Equal([100m, 0m, 200m], trend.Select(t => t.Spent));
        Assert.Equal(new YearMonth(2025, 1), trend[0].Month);
    }

    [Fact]
    public void Bank_fees_are_totalled_over_a_date_range()
    {
        var fees = _analyzer.BankFeesBetween(
            [T("2025-01-26", -69, "FEE", BankFees), T("2025-02-26", -69, "FEE", BankFees), T("2024-12-26", -69, "FEE", BankFees)],
            new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));

        Assert.Equal(138m, fees);
    }

    [Theory]
    [InlineData(950, "R 950")]
    [InlineData(12400, "R 12k")]
    [InlineData(1500, "R 1,5k")]
    [InlineData(1_250_000, "R 1,3m")]
    [InlineData(-2000, "-R 2k")]
    public void Compact_money_for_chart_axes(double amount, string expected)
    {
        Assert.Equal(expected, MoneyFormatter.FormatCompact((decimal)amount).Replace(MoneyFormatter.Space, ' '));
    }
}
