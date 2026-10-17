using BudgetApp.Core.Formatting;

namespace BudgetApp.Tests.Core;

public class MoneyFormatterTests
{
    // The formatter uses non-breaking spaces so amounts never wrap; compare with plain ones here.
    private static string Format(decimal amount, bool wholeUnits = false) =>
        MoneyFormatter.Format(amount, wholeUnits: wholeUnits).Replace(MoneyFormatter.Space, ' ');

    [Theory]
    [InlineData(1234.56, "R 1 234,56")]
    [InlineData(-1234.56, "-R 1 234,56")]
    [InlineData(0.5, "R 0,50")]
    [InlineData(1234567.891, "R 1 234 567,89")]
    public void Formats_rand_amounts(double amount, string expected)
    {
        Assert.Equal(expected, Format((decimal)amount));
    }

    [Fact]
    public void Whole_units_drop_the_cents()
    {
        Assert.Equal("R 1 240", Format(1239.6m, wholeUnits: true));
    }

    [Fact]
    public void Uses_non_breaking_spaces()
    {
        Assert.DoesNotContain(' ', MoneyFormatter.Format(1234.56m));
    }
}
