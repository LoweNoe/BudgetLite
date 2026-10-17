using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Models;
using BudgetApp.Import;

namespace BudgetApp.Tests.Core;

public class CategoriserTests
{
    private readonly RuleBasedCategoriser _categoriser = new();

    public CategoriserTests()
    {
        _categoriser.SetRules(BuiltInCatalog.CreateRules());
    }

    private string Categorise(string raw, decimal amount = -100m, string? mcc = null) =>
        _categoriser.Categorise(new CategorisationInput(DescriptionCleaner.Clean(raw), raw, amount, mcc)).CategoryId;

    [Theory]
    [InlineData("POS PURCHASE PNP CPT 1234 *4417", CategoryIds.Groceries)]
    [InlineData("WOOLWORTHS SANDTON", CategoryIds.Groceries)]
    [InlineData("CHECKERS HYPER", CategoryIds.Groceries)]
    [InlineData("KWIKSPAR LINDEN", CategoryIds.Groceries)]
    [InlineData("UBER EATS JOHANNESBURG", CategoryIds.Takeaways)]
    [InlineData("UBER *TRIP HELP.UBER.COM", CategoryIds.Transport)]
    [InlineData("MR D FOOD", CategoryIds.Takeaways)]
    [InlineData("NETFLIX.COM", CategoryIds.Subscriptions)]
    [InlineData("SHOWMAX", CategoryIds.Subscriptions)]
    [InlineData("SPOTIFY P1234", CategoryIds.Subscriptions)]
    [InlineData("VODACOM PREPAID", CategoryIds.AirtimeData)]
    [InlineData("MTN SP", CategoryIds.AirtimeData)]
    [InlineData("ENGEN QUICKSHOP", CategoryIds.Fuel)]
    [InlineData("SHELL ULTRA CITY", CategoryIds.Fuel)]
    [InlineData("#MONTHLY ACCOUNT FEE", CategoryIds.BankFees)]
    [InlineData("DIS-CHEM PHARMACY", CategoryIds.Health)]
    [InlineData("TAKEALOT.COM", CategoryIds.Shopping)]
    public void Recognises_common_south_african_merchants(string raw, string expected)
    {
        Assert.Equal(expected, Categorise(raw));
    }

    [Fact]
    public void Matches_whole_words_only()
    {
        Assert.Equal(CategoryIds.Uncategorised, Categorise("SPARKLE CLEANING CO"));
        Assert.Equal(CategoryIds.Uncategorised, Categorise("COFFEE SHOP"));
    }

    [Fact]
    public void Unknown_credits_fall_back_to_income_and_debits_to_uncategorised()
    {
        Assert.Equal(CategoryIds.Income, Categorise("SOMETHING ODD", 500m));
        Assert.Equal(CategoryIds.Uncategorised, Categorise("SOMETHING ODD", -500m));
    }

    [Fact]
    public void Salary_rules_only_apply_to_money_coming_in()
    {
        Assert.Equal(CategoryIds.Income, Categorise("SALARY ACME", 25000m));
        Assert.NotEqual(CategoryIds.Income, Categorise("SALARY ADVANCE REPAYMENT", -500m));
    }

    [Fact]
    public void Merchant_category_code_is_used_when_the_name_is_unknown()
    {
        Assert.Equal(CategoryIds.Takeaways, Categorise("CORNER CAFE", mcc: "5814"));
        Assert.Equal(CategoryIds.Groceries, Categorise("FARM STALL", mcc: "5411"));
    }

    [Fact]
    public void Merchant_names_beat_merchant_category_codes()
    {
        // Woolworths is coded as a department store, but we treat it as groceries.
        Assert.Equal(CategoryIds.Groceries, Categorise("WOOLWORTHS", mcc: "5311"));
    }

    [Fact]
    public void User_corrections_override_built_in_rules()
    {
        var rules = BuiltInCatalog.CreateRules();
        rules.Add(new CategoryRule
        {
            Pattern = "WOOLWORTHS SANDTON",
            CategoryId = CategoryIds.Shopping,
            Source = RuleSource.UserCorrection,
            Priority = 1000,
        });
        _categoriser.SetRules(rules);

        Assert.Equal(CategoryIds.Shopping, Categorise("POS PURCHASE WOOLWORTHS SANDTON *1234"));
        Assert.Equal(CategoryIds.Groceries, Categorise("WOOLWORTHS ROSEBANK"));
    }

    [Fact]
    public void Invalid_regex_rules_are_ignored()
    {
        _categoriser.SetRules([new CategoryRule { Pattern = "([", MatchType = RuleMatchType.Regex, CategoryId = CategoryIds.Health }]);

        Assert.Equal(CategoryIds.Uncategorised, Categorise("ANYTHING"));
    }
}
