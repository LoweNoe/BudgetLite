using BudgetApp.Import;

namespace BudgetApp.Tests.Import;

public class ValueParsersTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("-1234.56", -1234.56)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("R 1 234,56", 1234.56)]
    [InlineData("-R1,234.56", -1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("(250.00)", -250.00)]
    [InlineData("250.00 DR", -250.00)]
    [InlineData("250.00Cr", 250.00)]
    [InlineData("12,5", 12.5)]
    [InlineData("1,234", 1234)]
    [InlineData("99.99-", -99.99)]
    public void Parses_amounts(string input, double expected)
    {
        Assert.True(ValueParsers.TryParseAmount(input, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-")]
    public void Rejects_non_amounts(string input)
    {
        Assert.False(ValueParsers.TryParseAmount(input, out _));
    }

    [Theory]
    [InlineData("2025/03/14")]
    [InlineData("2025-03-14")]
    [InlineData("20250314")]
    [InlineData("14/03/2025")]
    [InlineData("14 Mar 2025")]
    [InlineData("14 March 2025")]
    [InlineData("14-Mar-2025")]
    [InlineData("2025-03-14 08:15:00")]
    public void Parses_common_bank_date_formats(string input)
    {
        Assert.True(ValueParsers.TryParseDate(input, out var date));
        Assert.Equal(new DateOnly(2025, 3, 14), date);
    }

    [Fact]
    public void Ambiguous_dates_are_read_day_first()
    {
        Assert.True(ValueParsers.TryParseDate("03/04/2025", out var date));
        Assert.Equal(new DateOnly(2025, 4, 3), date);
    }
}
