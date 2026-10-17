using System.Text.RegularExpressions;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace BudgetApp.Import.Pdf;

public sealed partial class PdfStatementParser : IStatementParser
{
    public const string ScannedMessage =
        "This PDF is a picture of the pages (a scan), so there's no text to read. Download the statement from online banking as a CSV, or as a regular PDF, and try again.";

    public const string NoTransactionsMessage =
        "We opened the PDF but couldn't find a transaction table in it. If your bank offers a CSV download, that will work too.";

    private static readonly (string Bank, Regex Pattern)[] Banks =
    [
        ("FNB", BankPattern(@"\bFNB\b|First National Bank")),
        ("Standard Bank", BankPattern(@"Standard Bank")),
        ("Absa", BankPattern(@"\bAbsa\b")),
        ("Nedbank", BankPattern(@"\bNedbank\b")),
        ("Capitec", BankPattern(@"\bCapitec\b")),
        ("Discovery Bank", BankPattern(@"Discovery Bank")),
        ("TymeBank", BankPattern(@"\bTyme\s?Bank\b")),
        ("African Bank", BankPattern(@"African Bank")),
        ("Investec", BankPattern(@"\bInvestec\b")),
    ];

    public string Name => "PDF statement";
    public int Specificity => 0;

    public bool CanParse(StatementFile file) => file.LooksLikePdf;

    public ParseResult Parse(StatementFile file, ColumnMapping? mapping = null)
    {
        PdfDocument document;
        try
        {
            var options = new ParsingOptions { UseLenientParsing = true };
            if (!string.IsNullOrEmpty(file.Password))
                options.Password = file.Password;
            document = PdfDocument.Open(file.Content, options);
        }
        catch (PdfDocumentEncryptedException)
        {
            return new ParseResult
            {
                Status = ParseStatus.NeedsPassword,
                Message = string.IsNullOrEmpty(file.Password)
                    ? "This PDF is locked with a password."
                    : "That password didn't open the PDF. Check it and try again.",
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ParseResult.Failed("This PDF couldn't be opened. It may be damaged; try downloading it again.");
        }

        using (document)
        {
            var lines = new List<LayoutLine>();
            foreach (var page in document.GetPages())
            {
                var words = page.GetWords()
                    .Where(w => w.TextOrientation == TextOrientation.Horizontal && !string.IsNullOrWhiteSpace(w.Text))
                    .Select(w => new LayoutWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, Math.Max(1, w.BoundingBox.Height)));
                lines.AddRange(LayoutLine.Group(words, page.Number));
            }

            if (lines.Count == 0)
                return ParseResult.Failed(ScannedMessage);

            var table = StatementTableReader.Read(lines);
            if (table.Transactions.Count == 0)
                return ParseResult.Failed(NoTransactionsMessage);

            return new ParseResult
            {
                Status = ParseStatus.Success,
                Transactions = table.Transactions,
                ParserName = Name,
                BankName = DetectBank(lines),
                BalanceCheck = table.BalanceCheck,
                Warnings = table.Warnings,
                SkippedRows = table.SkippedLines,
            };
        }
    }

    public static string? DetectBank(IEnumerable<LayoutLine> lines)
    {
        var text = string.Join('\n', lines.Take(60).Select(l => l.Text));
        return Banks.FirstOrDefault(b => b.Pattern.IsMatch(text)).Bank;
    }

    private static Regex BankPattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
