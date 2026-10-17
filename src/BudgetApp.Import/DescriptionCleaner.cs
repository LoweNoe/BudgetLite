using System.Text.RegularExpressions;

namespace BudgetApp.Import;

public static partial class DescriptionCleaner
{
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var text = raw;
        text = NoisePhrases().Replace(text, " ");
        text = CardMasks().Replace(text, " ");
        // Order matters: "14:32" and "R450.00" would otherwise be half-eaten by the date pattern.
        text = Times().Replace(text, " ");
        text = MoneyAmounts().Replace(text, " ");
        text = Dates().Replace(text, " ");
        text = ReferenceCodes().Replace(text, " ");
        text = ReferenceLabels().Replace(text, " ");
        text = Whitespace().Replace(text, " ").Trim(' ', '-', '*', '/', ':', '#', '.', ',');

        return text.Length > 0 ? text : Whitespace().Replace(raw, " ").Trim();
    }

    [GeneratedRegex(@"\b(?:POS\s+(?:LOCAL\s+|INT(?:ERNATIONAL)?\s+)?PURCHASE|(?:DEBIT|CHEQUE|CREDIT|VISA|MASTERCARD)\s+CARD\s+PURCHASE|CARD\s+PURCHASE|(?:ONLINE|LOCAL|CONTACTLESS|RECURRING|INTERNATIONAL)\s+PURCHASE|PURCHASE|DEBIT\s+ORDER|MAGTAPE\s+DEBIT|DEBICHECK|CARD\s+TRANSACTION)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoisePhrases();

    // *4417, 489537******1234, XXXX1234
    [GeneratedRegex(@"\d{4,6}\*{2,}\d{2,4}|\*+\d{2,6}\b|\bX{2,}\d{2,4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CardMasks();

    [GeneratedRegex(@"\b\d{1,4}[/\-.]\d{1,2}(?:[/\-.]\d{2,4})?\b|\b\d{1,2}\s?(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)[A-Z]*(?:\s?\d{2,4})?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Dates();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(?::\d{2})?\b")]
    private static partial Regex Times();

    [GeneratedRegex(@"\bR?\d+[.,]\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyAmounts();

    // Any token carrying a run of four or more digits: card numbers, terminal IDs, references.
    [GeneratedRegex(@"\b(?=[A-Z0-9]*\d{4})[A-Z0-9]{4,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceCodes();

    [GeneratedRegex(@"\b(?:REF(?:ERENCE)?|TRN|AUTH)\b[:#.]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceLabels();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
