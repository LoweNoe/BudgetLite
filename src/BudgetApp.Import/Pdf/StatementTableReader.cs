using System.Globalization;
using System.Text.RegularExpressions;
using BudgetApp.Core.Import;

namespace BudgetApp.Import.Pdf;

public sealed record TableReadResult(
    IReadOnlyList<RawTransaction> Transactions,
    BalanceCheck? BalanceCheck,
    IReadOnlyList<string> Warnings,
    int SkippedLines);

// Rebuilds a statement table from positioned words:
//  1. A header row ("Date  Description  Debit  Credit  Balance") gives each column an x-anchor.
//  2. A line with a date and at least one amount is a transaction; amounts go to the nearest anchor.
//  3. Lines with neither, sitting just under a transaction, continue its description.
//  4. The running balance confirms each amount and fixes signs the layout left ambiguous.
public static partial class StatementTableReader
{
    private const decimal Tolerance = 0.01m;
    private const int MaxContinuationLines = 2;
    private const int MaxWarnings = 5;

    private enum Kind { Date, Description, Amount, Debit, Credit, Balance, Fee }

    private sealed record Anchor(Kind Kind, double Left, double Right)
    {
        public double Center => (Left + Right) / 2;
    }

    private sealed record Token(string Text, double Left, double Right, double Height, int FirstWord, int WordCount)
    {
        public double Center => (Left + Right) / 2;
    }

    private sealed record DateHit(DateOnly Date, int Start, int Count, double Center);

    private sealed class Row
    {
        public DateOnly Date;
        public string Description = "";
        public decimal Amount;
        public bool SignKnown;
        public decimal? Balance;
        public int Page;
        public double Y;
        public double Height;
        public double DescriptionLeft;
        public int Continuations;
        public int Source;
    }

    private static readonly (string Phrase, Kind Kind)[] HeaderPhrases =
    [
        ("transaction date", Kind.Date), ("value date", Kind.Date), ("posting date", Kind.Date), ("trans date", Kind.Date),
        ("money out", Kind.Debit), ("paid out", Kind.Debit), ("money in", Kind.Credit), ("paid in", Kind.Credit),
        ("service fee", Kind.Fee), ("service fees", Kind.Fee), ("bank charges", Kind.Fee), ("accrued bank charges", Kind.Fee),
        ("running balance", Kind.Balance),
        ("date", Kind.Date), ("datum", Kind.Date),
        ("description", Kind.Description), ("details", Kind.Description), ("transaction", Kind.Description),
        ("narrative", Kind.Description), ("particulars", Kind.Description), ("beskrywing", Kind.Description), ("reference", Kind.Description),
        ("debit", Kind.Debit), ("debits", Kind.Debit), ("withdrawal", Kind.Debit), ("withdrawals", Kind.Debit), ("payments", Kind.Debit), ("debiet", Kind.Debit),
        ("credit", Kind.Credit), ("credits", Kind.Credit), ("deposit", Kind.Credit), ("deposits", Kind.Credit), ("receipts", Kind.Credit), ("krediet", Kind.Credit),
        ("amount", Kind.Amount), ("bedrag", Kind.Amount),
        ("balance", Kind.Balance), ("saldo", Kind.Balance),
        ("fee", Kind.Fee), ("fees", Kind.Fee), ("charge", Kind.Fee), ("charges", Kind.Fee), ("costs", Kind.Fee),
    ];

    public static TableReadResult Read(IReadOnlyList<LayoutLine> lines)
    {
        var reference = ReferenceDate(lines);
        var usesCrMarkers = lines.Any(l => l.Words.Any(w => CreditMarker().IsMatch(w.Text)));

        var rows = new List<Row>();
        var warnings = new List<string>();
        List<Anchor>? anchors = null;
        decimal? opening = null;
        Row? last = null;
        var skipped = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var tokens = Merge(line.Words);

            if (ReadHeader(tokens) is { } header)
            {
                anchors = header;
                last = null;
                continue;
            }

            var lower = line.Text.ToLowerInvariant();
            if (OpeningBalance().IsMatch(lower))
            {
                var balanceToken = tokens.LastOrDefault(IsAmount);
                if (opening is null && balanceToken is not null && ValueParsers.TryParseAmount(balanceToken.Text, out var value))
                    opening = value;
                last = null;
                continue;
            }

            if (ClosingOrTotal().IsMatch(lower))
            {
                last = null;
                continue;
            }

            var date = FindDate(tokens, anchors, reference);
            var amounts = tokens.Where((t, i) => IsAmount(t) && !InDate(i, date)).ToList();

            if (date is not null && amounts.Count > 0)
            {
                var row = BuildRow(line, tokens, date, amounts, anchors, usesCrMarkers, index);
                if (row is null)
                {
                    skipped++;
                    continue;
                }

                rows.Add(row);
                last = row;
                continue;
            }

            if (date is null && amounts.Count == 0 && last is not null && IsContinuation(line, last))
            {
                last.Description = $"{last.Description} {line.Text}".Trim();
                last.Continuations++;
                last.Y = line.Y;
                continue;
            }

            if (date is not null || amounts.Count > 0)
                skipped++;
            last = null;
        }

        var chronological = OrderChronologically(rows);
        var check = Reconcile(chronological, opening, warnings);

        var transactions = chronological
            .Select(r => new RawTransaction(r.Date, string.IsNullOrWhiteSpace(r.Description) ? "(no description)" : r.Description, r.Amount, r.Balance, null, r.Source))
            .ToList();

        return new TableReadResult(transactions, check, warnings, skipped);
    }

    private static Row? BuildRow(LayoutLine line, List<Token> tokens, DateHit date, List<Token> amounts, List<Anchor>? anchors, bool usesCrMarkers, int index)
    {
        var used = new HashSet<Token>();
        decimal? amount = null;
        var signKnown = false;
        decimal? balance = null;

        var money = anchors?.Where(a => a.Kind is Kind.Amount or Kind.Debit or Kind.Credit or Kind.Balance or Kind.Fee).ToList();
        if (money is { Count: > 0 })
        {
            // Each amount goes to the nearest money column; amounts are right-aligned, so right edges count.
            var assigned = new Dictionary<Kind, (Token Token, double Distance)>();
            foreach (var token in amounts)
            {
                var nearest = money
                    .Select(a => (Anchor: a, Distance: Math.Min(Math.Abs(token.Right - a.Right), Math.Abs(token.Center - a.Center))))
                    .MinBy(x => x.Distance);
                var reach = Math.Max(60, (nearest.Anchor.Right - nearest.Anchor.Left) * 2.5);
                if (nearest.Distance > reach)
                    continue;
                if (!assigned.TryGetValue(nearest.Anchor.Kind, out var existing) || nearest.Distance < existing.Distance)
                    assigned[nearest.Anchor.Kind] = (token, nearest.Distance);
            }

            foreach (var (kind, (token, _)) in assigned)
            {
                used.Add(token);
                if (!ValueParsers.TryParseAmount(token.Text, out var value))
                    continue;

                switch (kind)
                {
                    case Kind.Debit when value != 0:
                        amount = (amount ?? 0) - Math.Abs(value);
                        signKnown = true;
                        break;
                    case Kind.Credit when value != 0:
                        amount = (amount ?? 0) + Math.Abs(value);
                        signKnown = true;
                        break;
                    case Kind.Amount:
                        (amount, signKnown) = Signed(token.Text, value, usesCrMarkers);
                        break;
                    case Kind.Balance:
                        balance = value;
                        break;
                }
            }

            // Headers are sometimes mangled or missing a column. If no balance column was found but an
            // extra amount sits to the right of the transaction amount, that's almost always the balance.
            if (balance is null && !money.Any(a => a.Kind == Kind.Balance) && used.Count > 0)
            {
                var rightmostUsed = used.Max(t => t.Right);
                var extra = amounts.Where(t => !used.Contains(t) && t.Left > rightmostUsed).MaxBy(t => t.Right);
                if (extra is not null && ValueParsers.TryParseAmount(extra.Text, out var b))
                {
                    balance = b;
                    used.Add(extra);
                }
            }
        }
        else
        {
            // No header: the last amount is the running balance when there are two or more.
            var ordered = amounts.OrderBy(t => t.Left).ToList();
            var amountToken = ordered.Count >= 2 ? ordered[^2] : ordered[0];
            if (ordered.Count >= 2 && ValueParsers.TryParseAmount(ordered[^1].Text, out var b))
            {
                balance = b;
                used.Add(ordered[^1]);
            }

            if (ValueParsers.TryParseAmount(amountToken.Text, out var value))
            {
                (amount, signKnown) = Signed(amountToken.Text, value, usesCrMarkers);
                used.Add(amountToken);
            }
        }

        if (amount is null)
            return null;

        var description = tokens
            .Where((t, i) => !used.Contains(t) && !InDate(i, date) && !SignWord().IsMatch(t.Text) && !(IsAmount(t) && IsMoneyColumn(t, anchors)))
            .ToList();

        return new Row
        {
            Date = date.Date,
            Description = string.Join(' ', description.Select(t => t.Text)),
            Amount = amount.Value,
            SignKnown = signKnown,
            Balance = balance,
            Page = line.Page,
            Y = line.Y,
            Height = line.Height,
            DescriptionLeft = description.Count > 0 ? description[0].Left : line.Left,
            Source = index,
        };
    }

    // Without a header every amount counts as money; with one, an amount far from the money columns
    // (say "R29.00" inside "AIRTIME R29.00") is part of the description.
    private static bool IsMoneyColumn(Token token, List<Anchor>? anchors)
    {
        var money = anchors?.Where(a => a.Kind is Kind.Amount or Kind.Debit or Kind.Credit or Kind.Balance or Kind.Fee).ToList();
        if (money is not { Count: > 0 })
            return true;
        return money.Any(a => Math.Min(Math.Abs(token.Right - a.Right), Math.Abs(token.Center - a.Center)) <= Math.Max(60, (a.Right - a.Left) * 2.5));
    }

    // FNB-style statements mark credits with "Cr" and leave debits bare, so on those a bare amount is a debit.
    private static (decimal Amount, bool SignKnown) Signed(string text, decimal value, bool usesCrMarkers)
    {
        if (HasSignMarker(text))
            return (value, true);
        return (-Math.Abs(value), usesCrMarkers);
    }

    private static bool HasSignMarker(string text) =>
        text.Contains('-') || text.Contains('(') || CreditMarker().IsMatch(text) || DebitMarker().IsMatch(text);

    private static bool IsContinuation(LayoutLine line, Row last) =>
        line.Page == last.Page
        && last.Continuations < MaxContinuationLines
        // Glyph boxes are about 70% of the font size and lines sit about 1.4 font sizes apart.
        && last.Y - line.Y <= Math.Max(last.Height, line.Height) * 3
        && line.Left >= last.DescriptionLeft - 6;

    // Statements are printed oldest-first or newest-first. Whichever direction makes more balances add up wins.
    private static List<Row> OrderChronologically(List<Row> rows)
    {
        if (rows.Count < 2)
            return rows;

        int Score(IReadOnlyList<Row> list)
        {
            var score = 0;
            for (var i = 1; i < list.Count; i++)
            {
                if (list[i - 1].Balance is { } previous && list[i].Balance is { } current)
                {
                    var delta = current - previous;
                    if (Math.Abs(delta - list[i].Amount) <= Tolerance || (!list[i].SignKnown && Math.Abs(delta + list[i].Amount) <= Tolerance))
                        score++;
                }
            }

            return score;
        }

        var reversed = Enumerable.Reverse(rows).ToList();
        var forward = Score(rows);
        var backward = Score(reversed);
        if (backward > forward)
            return reversed;
        if (forward == 0 && backward == 0 && rows[0].Date > rows[^1].Date)
            return reversed;
        return rows;
    }

    private static BalanceCheck? Reconcile(List<Row> rows, decimal? opening, List<string> warnings)
    {
        var previous = opening;
        var checkedCount = 0;
        var mismatches = 0;

        foreach (var row in rows)
        {
            if (previous is { } before && row.Balance is { } after)
            {
                checkedCount++;
                var delta = after - before;
                if (Math.Abs(delta - row.Amount) <= Tolerance)
                {
                    row.SignKnown = true;
                }
                else if (Math.Abs(delta + row.Amount) <= Tolerance)
                {
                    row.Amount = -row.Amount;
                    row.SignKnown = true;
                }
                else
                {
                    mismatches++;
                    if (warnings.Count < MaxWarnings)
                        warnings.Add($"{row.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} · {row.Description}: the balance doesn't add up, so double-check this one.");
                }
            }

            previous = row.Balance ?? (previous is { } p ? p + row.Amount : null);
        }

        return checkedCount == 0 ? null : new BalanceCheck(checkedCount, mismatches);
    }

    private static List<Anchor>? ReadHeader(List<Token> tokens)
    {
        if (tokens.Count < 2 || tokens.Any(IsAmount))
            return null;

        var words = tokens.Select(t => Normalise(t.Text)).ToList();
        var anchors = new List<Anchor>();
        var seen = new HashSet<Kind>();

        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var (phrase, kind) in HeaderPhrases)
            {
                var parts = phrase.Split(' ');
                if (i + parts.Length > tokens.Count || !parts.Select((p, k) => words[i + k] == p).All(x => x))
                    continue;

                var left = tokens[i].Left;
                var right = tokens[i + parts.Length - 1].Right;

                // "Debit Amount" or "Credit Amount": the trailing word widens the column it belongs to.
                if (kind == Kind.Amount && anchors.Count > 0 && anchors[^1].Kind is Kind.Debit or Kind.Credit or Kind.Fee && left - anchors[^1].Right < tokens[i].Height * 2)
                {
                    anchors[^1] = anchors[^1] with { Right = right };
                }
                else if (seen.Add(kind))
                {
                    anchors.Add(new Anchor(kind, left, right));
                }

                i += parts.Length - 1;
                break;
            }
        }

        var kinds = anchors.Select(a => a.Kind).ToHashSet();
        var hasMoney = kinds.Contains(Kind.Balance) || kinds.Contains(Kind.Amount) || (kinds.Contains(Kind.Debit) && kinds.Contains(Kind.Credit));
        var hasContext = kinds.Contains(Kind.Date) || kinds.Contains(Kind.Description);
        return hasMoney && hasContext && kinds.Count >= 3 ? anchors : null;
    }

    private static DateHit? FindDate(List<Token> tokens, List<Anchor>? anchors, DateOnly reference)
    {
        var hits = new List<DateHit>();
        for (var i = 0; i < tokens.Count; i++)
        {
            for (var count = 3; count >= 1; count--)
            {
                if (i + count > tokens.Count)
                    continue;

                // Words of one date sit close together; a wide gap means separate columns.
                var span = tokens.Skip(i).Take(count).ToList();
                var maxGap = span.Max(t => t.Height) * 1.5;
                if (count > 1 && span.Zip(span.Skip(1), (a, b) => b.Left - a.Right).Any(gap => gap > maxGap))
                    continue;

                var text = string.Join(' ', span.Select(t => t.Text));
                if (TryParseDate(text, reference, out var date))
                {
                    hits.Add(new DateHit(date, i, count, (span[0].Left + span[^1].Right) / 2));
                    i += count - 1;
                    break;
                }
            }
        }

        if (hits.Count == 0)
            return null;

        var dateAnchor = anchors?.FirstOrDefault(a => a.Kind == Kind.Date);
        return dateAnchor is null ? hits[0] : hits.MinBy(h => Math.Abs(h.Center - dateAnchor.Center));
    }

    private static bool InDate(int index, DateHit? date) =>
        date is not null && index >= date.Start && index < date.Start + date.Count;

    // Joins "1" + "234.56" into "1 234.56", and "250.00" + "Cr" into "250.00Cr".
    private static List<Token> Merge(IReadOnlyList<LayoutWord> words)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var token = new Token(w.Text, w.Left, w.Right, w.Height, i, 1);

            while (i + 1 < words.Count)
            {
                var next = words[i + 1];
                var gap = next.Left - token.Right;
                if (gap < token.Height * 0.8 && ThousandsHead().IsMatch(token.Text) && ThousandsTail().IsMatch(next.Text))
                    token = token with { Text = $"{token.Text} {next.Text}", Right = next.Right, WordCount = token.WordCount + 1 };
                else if (gap < token.Height * 1.2 && IsAmount(token) && SignWord().IsMatch(next.Text))
                    token = token with { Text = token.Text + next.Text, Right = next.Right, WordCount = token.WordCount + 1 };
                else if (gap < token.Height * 0.8 && token.Text is "R" or "-" or "-R" && AmountPattern().IsMatch(next.Text))
                    token = token with { Text = token.Text + next.Text, Right = next.Right, WordCount = token.WordCount + 1 };
                else
                    break;
                i++;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static bool IsAmount(Token token) => AmountPattern().IsMatch(token.Text);

    // Yearless dates ("14 Mar", "14/03") take their year from the statement, stepping back a year
    // for December entries on a January statement.
    private static bool TryParseDate(string text, DateOnly reference, out DateOnly date)
    {
        if (!text.Any(char.IsAsciiDigit))
        {
            date = default;
            return false;
        }

        if (ValueParsers.TryParseDate(text, out date))
            return date.Year is >= 1990 and <= 2100;

        int day, month;
        if (DayMonthName().Match(text) is { Success: true } named
            && DateTime.TryParseExact(named.Groups[2].Value[..3], "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthName))
        {
            day = int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            month = monthName.Month;
        }
        else if (DayMonthNumber().Match(text) is { Success: true } numeric)
        {
            day = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(reference.Year, month))
            return false;

        date = new DateOnly(reference.Year, month, day);
        if (date > reference.AddDays(31))
            date = date.AddYears(-1);
        return true;
    }

    // The latest full date printed anywhere (usually the statement period end) anchors yearless dates.
    private static DateOnly ReferenceDate(IReadOnlyList<LayoutLine> lines)
    {
        var latest = DateOnly.MinValue;
        var ceiling = DateOnly.FromDateTime(DateTime.Today).AddYears(1);

        foreach (var line in lines)
        {
            var words = line.Words;
            for (var i = 0; i < words.Count; i++)
            {
                for (var count = 1; count <= 3 && i + count <= words.Count; count++)
                {
                    var text = string.Join(' ', words.Skip(i).Take(count).Select(w => w.Text));
                    if (text.Any(char.IsAsciiDigit) && ValueParsers.TryParseDate(text, out var date)
                        && date.Year >= 1990 && date <= ceiling && date > latest)
                        latest = date;
                }
            }
        }

        return latest == DateOnly.MinValue ? DateOnly.FromDateTime(DateTime.Today) : latest;
    }

    private static string Normalise(string text) => new(text.ToLowerInvariant().Where(char.IsLetter).ToArray());

    [GeneratedRegex(@"^\(?[-+]?R?\s?(?:\d{1,3}(?:[ , ]\d{3})+|\d+)[.,]\d{2}\)?\s?(?:[CcDd][Rr])?-?$")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^\(?[-+]?R?\d{1,3}$")]
    private static partial Regex ThousandsHead();

    [GeneratedRegex(@"^\d{3}(?:[ ,]\d{3})*(?:[.,]\d{2})?(?:[CcDd][Rr]|-|\))?$")]
    private static partial Regex ThousandsTail();

    [GeneratedRegex(@"^(?:[CcDd][Rr]|-)$")]
    private static partial Regex SignWord();

    [GeneratedRegex(@"\d\s?(?:Cr|CR)$|^(?:Cr|CR)$")]
    private static partial Regex CreditMarker();

    [GeneratedRegex(@"\d\s?(?:Dr|DR)$")]
    private static partial Regex DebitMarker();

    [GeneratedRegex(@"^(\d{1,2})\s?([A-Za-z]{3,9})$")]
    private static partial Regex DayMonthName();

    [GeneratedRegex(@"^(\d{1,2})[/-](\d{1,2})$")]
    private static partial Regex DayMonthNumber();

    [GeneratedRegex(@"opening balance|balance brought forward|brought forward|balance b/f|beginsaldo")]
    private static partial Regex OpeningBalance();

    [GeneratedRegex(@"closing balance|carried forward|balance c/f|^totals?\b|eindsaldo")]
    private static partial Regex ClosingOrTotal();
}
