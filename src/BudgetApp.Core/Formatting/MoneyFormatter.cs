using System.Globalization;

namespace BudgetApp.Core.Formatting;

public static class MoneyFormatter
{
    public const char Space = ' ';

    private static readonly NumberFormatInfo Format_ = new()
    {
        NumberGroupSeparator = Space.ToString(),
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    public static readonly IReadOnlyList<string> SupportedCurrencies = ["ZAR", "USD", "EUR", "GBP"];

    public static string Symbol(string currency) => currency.ToUpperInvariant() switch
    {
        "ZAR" => "R",
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        var other => other,
    };

    // Short labels for chart axes: R 950, R 12k, R 1,2m.
    public static string FormatCompact(decimal amount, string currency = "ZAR")
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0 ? "-" : "";
        var number = abs switch
        {
            >= 1_000_000 => Trim(abs / 1_000_000m) + "m",
            >= 1_000 => Trim(abs / 1_000m) + "k",
            _ => decimal.Round(abs).ToString("0", Format_),
        };
        return $"{sign}{Symbol(currency)}{Space}{number}";

        static string Trim(decimal value) => value >= 10
            ? decimal.Round(value, MidpointRounding.AwayFromZero).ToString("0", Format_)
            : decimal.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", Format_);
    }

    public static string Format(decimal amount, string currency = "ZAR", bool wholeUnits = false)
    {
        var rounded = decimal.Round(amount, wholeUnits ? 0 : 2, MidpointRounding.AwayFromZero);
        var number = Math.Abs(rounded).ToString(wholeUnits ? "#,0" : "#,0.00", Format_);
        var sign = rounded < 0 ? "-" : "";
        return $"{sign}{Symbol(currency)}{Space}{number}";
    }
}
