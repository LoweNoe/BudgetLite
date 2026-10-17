using System.Text;
using BudgetApp.Core.Import;

namespace BudgetApp.Import.Csv;

internal static class CsvLayoutDetector
{
    private const int ScanRows = 30;
    private static readonly char[] DelimiterCandidates = [',', ';', '\t', '|'];

    // The right delimiter splits most lines into the same number of fields.
    public static char DetectDelimiter(IReadOnlyList<string> lines)
    {
        var sample = lines.Where(l => l.Length > 0).Take(ScanRows).ToList();
        var best = ',';
        var bestScore = 0;

        foreach (var candidate in DelimiterCandidates)
        {
            var counts = sample.Select(l => CountOutsideQuotes(l, candidate)).Where(n => n > 0).ToList();
            if (counts.Count == 0)
                continue;

            var score = counts.GroupBy(n => n).Max(g => g.Count());
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    public static ColumnMapping? FromHeaders(IReadOnlyList<string[]> rows, char delimiter)
    {
        for (var i = 0; i < Math.Min(ScanRows, rows.Count); i++)
        {
            var mapping = MapHeaderRow(rows[i]) with { Delimiter = delimiter, HeaderRowIndex = i };
            if (mapping.IsComplete)
                return mapping;
        }

        return null;
    }

    // No recognisable header: infer columns from what the values look like.
    public static ColumnMapping? FromData(IReadOnlyList<string[]> rows, char delimiter)
    {
        var sample = rows.Where(r => r.Length >= 3).Take(50).ToList();
        if (sample.Count < 2)
            return null;

        var width = sample.Min(r => r.Length);
        var stats = Enumerable.Range(0, width).Select(col =>
        {
            var values = sample.Select(r => r[col]).ToList();
            return new
            {
                Column = col,
                DateRate = Rate(values, v => ValueParsers.TryParseDate(v, out _)),
                NumberRate = Rate(values, v => ValueParsers.TryParseAmount(v, out _) && !ValueParsers.TryParseDate(v, out _)),
                FilledRate = Rate(values, v => !string.IsNullOrWhiteSpace(v)),
                AverageLength = values.Average(v => v.Length),
            };
        }).ToList();

        var date = stats.Where(s => s.DateRate >= 0.8).OrderByDescending(s => s.DateRate).FirstOrDefault();
        if (date is null)
            return null;

        var numeric = stats.Where(s => s.Column != date.Column && s.NumberRate >= 0.8).Select(s => s.Column).ToList();
        var description = stats
            .Where(s => s.Column != date.Column && !numeric.Contains(s.Column) && s.FilledRate >= 0.8)
            .OrderByDescending(s => s.AverageLength)
            .FirstOrDefault();

        if (numeric.Count == 0 || description is null)
            return null;

        return new ColumnMapping
        {
            Delimiter = delimiter,
            HeaderRowIndex = -1,
            DateColumn = date.Column,
            DescriptionColumn = description.Column,
            AmountColumn = numeric[0],
            BalanceColumn = numeric.Count > 1 ? numeric[1] : null,
        };
    }

    // Best guess at which row holds the headers, used to label columns on the mapping screen.
    public static int GuessHeaderRow(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
            return -1;

        var modalWidth = rows.Take(ScanRows).GroupBy(r => r.Length).OrderByDescending(g => g.Count()).First().Key;
        for (var i = 0; i < Math.Min(ScanRows, rows.Count); i++)
        {
            var row = rows[i];
            if (row.Length == modalWidth && row.Count(c => ValueParsers.TryParseAmount(c, out _)) == 0 && row.Any(c => c.Length > 0))
                return i;
        }

        return -1;
    }

    private static ColumnMapping MapHeaderRow(string[] cells)
    {
        int? date = null, amount = null, debit = null, credit = null, balance = null, mcc = null;
        var description = (Column: -1, Score: 0);

        for (var i = 0; i < cells.Length; i++)
        {
            var h = Normalise(cells[i]);
            if (h.Length == 0)
                continue;

            if (mcc is null && (h == "mcc" || h.Contains("merchant category")))
                mcc = i;
            else if (date is null && (h.Contains("date") || h.Contains("datum")))
                date = i;
            else if (balance is null && (h.Contains("balance") || h.Contains("saldo")))
                balance = i;
            else if (debit is null && (h.Contains("debit") || h.Contains("money out") || h.Contains("withdrawal") || h is "out" or "paid out"))
                debit = i;
            else if (credit is null && (h.Contains("credit") || h.Contains("money in") || h.Contains("deposit") || h is "in" or "paid in"))
                credit = i;
            else if (amount is null && (h.Contains("amount") || h.Contains("bedrag") || h is "value" or "amt"))
                amount = i;
            else
            {
                var score = DescriptionScore(h);
                if (score > description.Score)
                    description = (i, score);
            }
        }

        return new ColumnMapping
        {
            DateColumn = date ?? -1,
            DescriptionColumn = description.Column,
            AmountColumn = amount,
            DebitColumn = amount is null ? debit : null,
            CreditColumn = amount is null ? credit : null,
            BalanceColumn = balance,
            MerchantCategoryCodeColumn = mcc,
        };
    }

    private static int DescriptionScore(string header)
    {
        if (header.Contains("type") || header.Contains("code"))
            return 0;
        if (header.Contains("description") || header.Contains("beskrywing"))
            return 3;
        if (header.Contains("details") || header.Contains("narrative") || header.Contains("particulars") || header.Contains("transaction"))
            return 2;
        if (header.Contains("reference") || header.Contains("memo") || header.Contains("payee") || header.Contains("beneficiary"))
            return 1;
        return 0;
    }

    private static string Normalise(string header)
    {
        var sb = new StringBuilder(header.Length);
        foreach (var c in header.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static double Rate(List<string> values, Func<string, bool> predicate) =>
        values.Count == 0 ? 0 : values.Count(predicate) / (double)values.Count;

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }

        return count;
    }
}
