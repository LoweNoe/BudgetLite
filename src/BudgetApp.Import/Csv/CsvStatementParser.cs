using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Import;
using CsvHelper;
using CsvHelper.Configuration;

namespace BudgetApp.Import.Csv;

public sealed partial class CsvStatementParser : IStatementParser
{
    private const int SampleRowCount = 5;

    public string Name => "CSV statement";
    public int Specificity => 0;

    public bool CanParse(StatementFile file)
    {
        return file.Extension is ".csv" or ".txt" && !file.LooksLikePdf;
    }

    public ParseResult Parse(StatementFile file, ColumnMapping? mapping = null)
    {
        var text = Decode(file.Content);
        var lines = text.Split('\n');
        var delimiter = mapping?.Delimiter ?? CsvLayoutDetector.DetectDelimiter(lines);
        var rows = ReadRows(text, delimiter);

        if (rows.Count == 0)
            return ParseResult.Failed("This file looks empty.");

        mapping ??= CsvLayoutDetector.FromHeaders(rows, delimiter) ?? CsvLayoutDetector.FromData(rows, delimiter);
        if (mapping is null || !mapping.IsComplete)
            return NeedsMapping(rows, delimiter, mapping, "We couldn't tell which columns hold the date, description and amount.");

        var transactions = new List<RawTransaction>();
        var skipped = 0;

        for (var i = mapping.HeaderRowIndex + 1; i < rows.Count; i++)
        {
            if (TryReadRow(rows[i], i, mapping) is { } row)
                transactions.Add(row);
            else
                skipped++;
        }

        if (transactions.Count == 0)
            return NeedsMapping(rows, delimiter, mapping, "No transactions could be read using these columns.");

        return new ParseResult
        {
            Status = ParseStatus.Success,
            Transactions = transactions,
            Mapping = mapping,
            Headers = HeadersFor(rows, mapping.HeaderRowIndex),
            SampleRows = SampleFor(rows, mapping.HeaderRowIndex),
            SkippedRows = skipped,
            ParserName = Name,
        };
    }

    private static RawTransaction? TryReadRow(string[] cells, int rowIndex, ColumnMapping mapping)
    {
        string Cell(int? column) => column is { } c && c >= 0 && c < cells.Length ? cells[c] : "";

        if (!ValueParsers.TryParseDate(Cell(mapping.DateColumn), out var date))
            return null;

        var description = Cell(mapping.DescriptionColumn).Trim();
        if (BalanceLine().IsMatch(description))
            return null;

        decimal amount;
        if (ValueParsers.TryParseAmount(Cell(mapping.AmountColumn), out var single))
        {
            amount = single;
        }
        else
        {
            var hasDebit = ValueParsers.TryParseAmount(Cell(mapping.DebitColumn), out var debit) && debit != 0;
            var hasCredit = ValueParsers.TryParseAmount(Cell(mapping.CreditColumn), out var credit) && credit != 0;
            if (!hasDebit && !hasCredit)
                return null;
            amount = (hasCredit ? Math.Abs(credit) : 0) - (hasDebit ? Math.Abs(debit) : 0);
        }

        decimal? balance = ValueParsers.TryParseAmount(Cell(mapping.BalanceColumn), out var b) ? b : null;
        var mcc = Cell(mapping.MerchantCategoryCodeColumn).Trim();

        return new RawTransaction(
            date,
            description.Length > 0 ? description : "(no description)",
            amount,
            balance,
            mcc.Length > 0 ? mcc : null,
            rowIndex);
    }

    private static ParseResult NeedsMapping(List<string[]> rows, char delimiter, ColumnMapping? partial, string message)
    {
        var headerRow = partial?.HeaderRowIndex is >= 0 ? partial.HeaderRowIndex : CsvLayoutDetector.GuessHeaderRow(rows);
        return new ParseResult
        {
            Status = ParseStatus.NeedsMapping,
            Mapping = (partial ?? new ColumnMapping()) with { Delimiter = delimiter, HeaderRowIndex = headerRow },
            Headers = HeadersFor(rows, headerRow),
            SampleRows = SampleFor(rows, headerRow),
            Message = message,
        };
    }

    private static IReadOnlyList<string> HeadersFor(List<string[]> rows, int headerRow)
    {
        var width = rows.Max(r => r.Length);
        var header = headerRow >= 0 && headerRow < rows.Count ? rows[headerRow] : [];
        return Enumerable.Range(0, width)
            .Select(i => i < header.Length && header[i].Trim().Length > 0 ? header[i].Trim() : $"Column {i + 1}")
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<string>> SampleFor(List<string[]> rows, int headerRow) =>
        rows.Skip(headerRow + 1).Where(r => r.Length > 1).Take(SampleRowCount).Select(r => (IReadOnlyList<string>)r).ToList();

    private static List<string[]> ReadRows(string text, char delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false,
        };

        var rows = new List<string[]>();
        using var reader = new StringReader(text);
        using var parser = new CsvParser(reader, config);
        while (parser.Read())
        {
            if (parser.Record is { Length: > 0 } record && record.Any(c => c.Length > 0))
                rows.Add(record);
        }

        return rows;
    }

    // Most bank exports are UTF-8; older ones are Windows-1252, which Latin-1 covers well enough.
    private static string Decode(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Contains('�') ? Encoding.Latin1.GetString(content) : text;
    }

    [GeneratedRegex(@"^(?:opening|closing)\s+balance|^balance\s+(?:brought|carried)", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceLine();
}
