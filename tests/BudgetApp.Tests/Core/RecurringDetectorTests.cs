using BudgetApp.Core.Analysis;
using BudgetApp.Core.Models;
using static BudgetApp.Core.Models.CategoryIds;
using static BudgetApp.Tests.Core.AnalysisTestData;

namespace BudgetApp.Tests.Core;

public class RecurringDetectorTests
{
    private static readonly DateOnly AsOf = new(2025, 3, 31);

    [Fact]
    public void Finds_a_monthly_subscription()
    {
        var charges = RecurringDetector.Detect(
        [
            T("2025-01-05", -199, "NETFLIX.COM", Subscriptions),
            T("2025-02-05", -199, "NETFLIX.COM", Subscriptions),
            T("2025-03-05", -199, "NETFLIX.COM", Subscriptions),
        ], AsOf);

        var netflix = Assert.Single(charges);
        Assert.Equal(RecurringKind.Subscription, netflix.Kind);
        Assert.Equal(RecurringCadence.Monthly, netflix.Cadence);
        Assert.Equal(199m, netflix.Amount);
        Assert.Equal(new DateOnly(2025, 4, 5), netflix.NextDate);
        Assert.Equal("Netflix.com", netflix.Merchant);
        Assert.Equal(3, netflix.TransactionIds.Count);
    }

    [Fact]
    public void Tolerates_a_price_increase_on_subscriptions()
    {
        var charges = RecurringDetector.Detect(
        [
            T("2025-01-14", -59.99m, "SPOTIFY", Subscriptions),
            T("2025-02-14", -59.99m, "SPOTIFY", Subscriptions),
            T("2025-03-14", -69.99m, "SPOTIFY", Subscriptions),
        ], AsOf);

        Assert.Single(charges);
    }

    [Fact]
    public void Debit_orders_are_recognised_from_the_raw_description()
    {
        var charges = RecurringDetector.Detect(
        [
            T("2025-02-01", -899, "DSTV", Utilities, raw: "DEBIT ORDER DSTV 1234"),
            T("2025-03-01", -899, "DSTV", Utilities, raw: "DEBIT ORDER DSTV 1234"),
        ], AsOf);

        Assert.Equal(RecurringKind.DebitOrder, Assert.Single(charges).Kind);
    }

    [Fact]
    public void Finds_weekly_regular_payments_with_steady_amounts()
    {
        var charges = RecurringDetector.Detect(
        [
            T("2025-03-03", -150, "GYM CO", Health),
            T("2025-03-10", -150, "GYM CO", Health),
            T("2025-03-17", -150, "GYM CO", Health),
            T("2025-03-24", -150, "GYM CO", Health),
        ], AsOf);

        var gym = Assert.Single(charges);
        Assert.Equal(RecurringCadence.Weekly, gym.Cadence);
        Assert.Equal(RecurringKind.Regular, gym.Kind);
        Assert.Equal(650m, gym.MonthlyCost);
    }

    [Fact]
    public void Ignores_regular_shops_with_varying_amounts()
    {
        var charges = RecurringDetector.Detect(
        [
            T("2025-01-01", -1320, "PNP CPT", Groceries),
            T("2025-02-01", -880, "PNP CPT", Groceries),
            T("2025-03-01", -1490, "PNP CPT", Groceries),
        ], AsOf);

        Assert.Empty(charges);
    }

    [Fact]
    public void Two_hits_are_not_enough_for_an_ordinary_payment()
    {
        Assert.Empty(RecurringDetector.Detect(
        [
            T("2025-02-20", -550, "GYM CO", Health),
            T("2025-03-20", -550, "GYM CO", Health),
        ], AsOf));
    }

    [Fact]
    public void Lapsed_subscriptions_are_dropped()
    {
        Assert.Empty(RecurringDetector.Detect(
        [
            T("2024-10-05", -99, "SHOWMAX", Subscriptions),
            T("2024-11-05", -99, "SHOWMAX", Subscriptions),
        ], AsOf));
    }

    [Fact]
    public void Branches_of_the_same_merchant_group_together()
    {
        Assert.Equal("VIDA E CAFFE", RecurringDetector.MerchantKey(T("2025-01-01", -1, "VIDA E CAFFE ROSEBANK 2", Takeaways)));
        Assert.Equal("NETFLIX COM", RecurringDetector.MerchantKey(T("2025-01-01", -1, "NETFLIX.COM", Subscriptions)));
    }

    [Theory]
    [InlineData("NETFLIX.COM", "Netflix.com")]
    [InlineData("PNP CPT", "PNP CPT")]
    [InlineData("KFC BRYANSTON", "KFC Bryanston")]
    [InlineData("Already Mixed", "Already Mixed")]
    [InlineData("IB PAYMENT TO LANDLORD RENT", "IB Payment to Landlord Rent")]
    [InlineData("ACME HOLDINGS PTY LTD", "Acme Holdings Pty Ltd")]
    public void Title_cases_bank_descriptions(string input, string expected)
    {
        Assert.Equal(expected, TextCase.Title(input));
    }
}
