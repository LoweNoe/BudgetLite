using System.Text;
using System.Text.RegularExpressions;
using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public static partial class RecurringDetector
{
    private const double RequiredAgreement = 0.75;

    private static readonly (RecurringCadence Cadence, int Min, int Max, int Nominal)[] Cadences =
    [
        (RecurringCadence.Weekly, 6, 8, 7),
        (RecurringCadence.Fortnightly, 13, 16, 14),
        (RecurringCadence.Monthly, 26, 35, 30),
        (RecurringCadence.Quarterly, 84, 98, 91),
        (RecurringCadence.Yearly, 350, 380, 365),
    ];

    // Groups debits by merchant, then keeps groups whose gaps match a regular cadence and whose
    // amounts stay close to the median. Subscriptions and debit orders may drift in price (annual
    // increases), so they get a looser amount tolerance than other regular payments, which also
    // need at least three hits before we trust them.
    public static IReadOnlyList<RecurringCharge> Detect(IEnumerable<Transaction> transactions, DateOnly asOf)
    {
        var results = new List<RecurringCharge>();

        var groups = transactions
            .Where(t => t.Amount < 0 && t.CategoryId != CategoryIds.Income)
            .GroupBy(t => MerchantKey(t))
            .Where(g => g.Key.Length > 0 && g.Count() >= 2);

        foreach (var group in groups)
        {
            var items = group.OrderBy(t => t.Date).ToList();
            var kind = KindOf(items);
            var minimum = kind == RecurringKind.Regular ? 3 : 2;
            if (items.Count < minimum)
                continue;

            if (MatchCadence(items.Select(t => t.Date).ToList()) is not { } cadence)
                continue;

            var tolerance = kind == RecurringKind.Regular ? 0.07m : 0.2m;
            if (!AmountsAgree(items.Select(t => -t.Amount).ToList(), tolerance, out var typical))
                continue;

            var last = items[^1];
            if (IsLapsed(last.Date, cadence.Nominal, asOf))
                continue;

            var next = NextDate(last.Date, cadence.Cadence);
            while (next <= asOf)
                next = NextDate(next, cadence.Cadence);

            results.Add(new RecurringCharge(
                DisplayName(last),
                MostCommon(items.Select(t => t.CategoryId)),
                kind,
                cadence.Cadence,
                decimal.Round(typical, 2),
                decimal.Round(MonthlyCost(typical, cadence.Cadence), 2),
                items[0].Date,
                last.Date,
                next,
                items.Count,
                items.Select(t => t.Id).ToList()));
        }

        return results.OrderByDescending(r => r.MonthlyCost).ToList();
    }

    // The gaps between dates must mostly fit one cadence, judged by the median gap.
    internal static (RecurringCadence Cadence, int Nominal)? MatchCadence(IReadOnlyList<DateOnly> sortedDates)
    {
        var gaps = sortedDates.Zip(sortedDates.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Where(g => g > 0).ToList();
        if (gaps.Count == 0)
            return null;

        var medianGap = Median(gaps);
        var cadence = Cadences.FirstOrDefault(c => medianGap >= c.Min && medianGap <= c.Max);
        if (cadence == default || gaps.Count(g => g >= cadence.Min && g <= cadence.Max) < gaps.Count * RequiredAgreement)
            return null;

        return (cadence.Cadence, cadence.Nominal);
    }

    internal static bool AmountsAgree(List<decimal> amounts, decimal tolerance, out decimal typical)
    {
        typical = Median(amounts);
        var median = typical;
        return amounts.Count(a => Math.Abs(a - median) <= median * tolerance) >= amounts.Count * RequiredAgreement;
    }

    internal static bool IsLapsed(DateOnly last, int nominalGap, DateOnly asOf) =>
        asOf.DayNumber - last.DayNumber > (int)(nominalGap * 1.6);

    public static DateOnly NextDate(DateOnly from, RecurringCadence cadence) => cadence switch
    {
        RecurringCadence.Weekly => from.AddDays(7),
        RecurringCadence.Fortnightly => from.AddDays(14),
        RecurringCadence.Monthly => from.AddMonths(1),
        RecurringCadence.Quarterly => from.AddMonths(3),
        _ => from.AddYears(1),
    };

    public static decimal MonthlyCost(decimal amount, RecurringCadence cadence) => cadence switch
    {
        RecurringCadence.Weekly => amount * 52 / 12,
        RecurringCadence.Fortnightly => amount * 26 / 12,
        RecurringCadence.Monthly => amount,
        RecurringCadence.Quarterly => amount / 3,
        _ => amount / 12,
    };

    // First three words of the cleaned description with digits stripped, so "VIDA E CAFFE ROSEBANK"
    // and "VIDA E CAFFE SANDTON" land together while different merchants stay apart.
    public static string MerchantKey(Transaction t)
    {
        var source = string.IsNullOrWhiteSpace(t.CleanDescription) ? t.Description : t.CleanDescription;
        var letters = NonLetters().Replace(source.ToUpperInvariant(), " ");
        return string.Join(' ', letters.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
    }

    private static RecurringKind KindOf(List<Transaction> items)
    {
        if (items.Any(t => t.CategoryId == CategoryIds.Subscriptions))
            return RecurringKind.Subscription;
        if (items.Any(t => DebitOrderMarker().IsMatch(t.Description)))
            return RecurringKind.DebitOrder;
        return RecurringKind.Regular;
    }

    internal static string DisplayName(Transaction t)
    {
        var name = string.IsNullOrWhiteSpace(t.CleanDescription) ? t.Description : t.CleanDescription;
        return TextCase.Title(name);
    }

    private static string MostCommon(IEnumerable<string> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;

    private static int Median(List<int> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    internal static decimal Median(List<decimal> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    [GeneratedRegex(@"[^A-Z&]+")]
    private static partial Regex NonLetters();

    [GeneratedRegex(@"DEBIT\s+ORDER|DEBICHECK|MAGTAPE|\bD/?O\b", RegexOptions.IgnoreCase)]
    private static partial Regex DebitOrderMarker();
}

public static class TextCase
{
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "of", "and", "the", "for", "at", "on", "in", "by", "from", "via",
    };

    // Short words that are ordinary words, not acronyms, so they get normal capitalisation.
    private static readonly HashSet<string> ShortWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "pty", "ltd", "inc", "co", "cc", "sal", "box", "pay", "fee", "new",
    };

    // "NETFLIX.COM" -> "Netflix.com", but short all-caps names like PNP, KFC and MTN stay as they are.
    public static string Title(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsLower))
            return text;

        var sb = new StringBuilder(text.Length);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0)
                sb.Append(' ');

            var letters = word.Count(char.IsLetter);
            if (sb.Length > 0 && SmallWords.Contains(word))
                sb.Append(word.ToLowerInvariant());
            else if (letters <= 3 && word.Length <= 4 && !ShortWords.Contains(word))
                sb.Append(word);
            else
                sb.Append(word[0]).Append(word[1..].ToLowerInvariant());
        }

        return sb.ToString();
    }
}
