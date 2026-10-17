using BudgetApp.Import;

namespace BudgetApp.Tests.Import;

public class DescriptionCleanerTests
{
    [Theory]
    [InlineData("POS PURCHASE PNP CPT 1234 *4417", "PNP CPT")]
    [InlineData("POS LOCAL PURCHASE WOOLWORTHS SANDTON 489537******1234", "WOOLWORTHS SANDTON")]
    [InlineData("CARD PURCHASE UBER EATS 12/03/2025 *8812", "UBER EATS")]
    [InlineData("DEBIT ORDER NETFLIX.COM REF 99812374", "NETFLIX.COM")]
    [InlineData("CHECKERS HYPER 05 MAR 14:32 XXXX5521", "CHECKERS HYPER")]
    [InlineData("ENGEN QUICKSHOP R450.00 2025-03-01", "ENGEN QUICKSHOP")]
    [InlineData("Spotify AB", "Spotify AB")]
    public void Strips_noise_and_keeps_the_merchant(string raw, string expected)
    {
        Assert.Equal(expected, DescriptionCleaner.Clean(raw));
    }

    [Fact]
    public void Falls_back_to_the_raw_text_when_everything_is_noise()
    {
        Assert.Equal("POS PURCHASE 1234", DescriptionCleaner.Clean("  POS   PURCHASE 1234 "));
    }

    [Fact]
    public void Empty_input_gives_empty_output()
    {
        Assert.Equal("", DescriptionCleaner.Clean("   "));
    }
}
