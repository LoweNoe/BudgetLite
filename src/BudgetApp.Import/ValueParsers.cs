using System.Globalization;

namespace BudgetApp.Import;

public static class ValueParsers
{
    // South African statements are day-first, so ambiguous dates like 03/04/2025 read as 3 April.
    private static readonly string[] DateFormats =
    [
        "yyyy/MM/dd", "yyyy-MM-dd", "yyyy.MM.dd", "yyyyMMdd",
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy",
        "dd/MM/yy", "d/M/yy",
        "dd MMM yyyy", "d MMM yyyy", "dd MMMM yyyy", "d MMMM yyyy", "dd MMM yy", "d MMM yy",
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd-MMM-yy",
    ];

    public static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        var timeStart = text.IndexOfAny([' ', 'T'], Math.Min(8, text.Length));
        if (timeStart > 0 && text.IndexOf(':', timeStart) > 0)
            text = text[..timeStart];

        return DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    public static bool TryParseAmount(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim().ToUpperInvariant();
        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1];
        }

        if (text.EndsWith("DR"))
        {
            negative = true;
            text = text[..^2];
        }
        else if (text.EndsWith("CR"))
        {
            text = text[..^2];
        }

        var chars = text.Where(c => char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '+').ToArray();
        text = new string(chars);

        if (text.StartsWith('-') || text.EndsWith('-'))
            negative = true;
        text = text.Trim('-', '+');

        if (text.Length == 0 || !text.Any(char.IsAsciiDigit))
            return false;

        text = NormaliseSeparators(text);
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;

        if (negative)
            amount = -amount;
        return true;
    }

    // Works out which of ',' and '.' is the decimal separator: "1 234,56", "1,234.56", "1234.56", "12,5".
    private static string NormaliseSeparators(string text)
    {
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            return lastComma > lastDot
                ? text.Replace(".", "").Replace(',', '.')
                : text.Replace(",", "");
        }

        if (lastComma >= 0)
        {
            var single = text.IndexOf(',') == lastComma;
            var digitsAfter = text.Length - lastComma - 1;
            return single && digitsAfter != 3 ? text.Replace(',', '.') : text.Replace(",", "");
        }

        if (lastDot >= 0 && text.IndexOf('.') != lastDot)
            return text.Replace(".", "");

        return text;
    }
}
