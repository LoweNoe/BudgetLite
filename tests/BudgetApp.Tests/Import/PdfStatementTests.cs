using BudgetApp.Core.Import;
using BudgetApp.Import.Pdf;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace BudgetApp.Tests.Import;

public class PdfStatementTests
{
    private readonly PdfStatementParser _parser = new();

    // Builds simple statement PDFs. Amount columns are right-aligned like real statements.
    private sealed class StatementPdf
    {
        private const double FontSize = 9;
        private readonly PdfDocumentBuilder _builder = new();
        private readonly PdfDocumentBuilder.AddedFont _font;
        private PdfPageBuilder _page;
        private double _y = 790;

        public StatementPdf()
        {
            _font = _builder.AddStandard14Font(Standard14Font.Helvetica);
            _page = _builder.AddPage(PageSize.A4);
        }

        public StatementPdf Line(params (double X, string Text)[] cells) => Line(FontSize + 6, cells);

        // Negative X means "right-aligned to this edge".
        public StatementPdf Line(double advance, params (double X, string Text)[] cells)
        {
            foreach (var (x, text) in cells)
            {
                var left = x >= 0 ? x : -x - text.Length * FontSize * 0.52;
                _page.AddText(text, FontSize, new PdfPoint(left, _y), _font);
            }

            _y -= advance;
            return this;
        }

        public StatementPdf NewPage()
        {
            _page = _builder.AddPage(PageSize.A4);
            _y = 790;
            return this;
        }

        public StatementFile Build(string name = "statement.pdf") => new(name, _builder.Build());
    }

    [Fact]
    public void Reads_debit_and_credit_columns_and_checks_the_balance()
    {
        var file = new StatementPdf()
            .Line((40, "Cheque Account Statement"))
            .Line((40, "Date"), (110, "Description"), (-360, "Debit"), (-440, "Credit"), (-540, "Balance"))
            .Line((40, "2026/03/01"), (110, "Opening Balance"), (-540, "10 000.00"))
            .Line((40, "2026/03/02"), (110, "POS PURCHASE PNP CPT 1234 *4417"), (-360, "245.50"), (-540, "9 754.50"))
            .Line((40, "2026/03/03"), (110, "SALARY ACME"), (-440, "25 000.00"), (-540, "34 754.50"))
            .Line((40, "2026/03/04"), (110, "VODACOM AIRTIME R29.00"), (-360, "29.00"), (-540, "34 725.50"))
            .Line((40, "2026/03/05"), (110, "UBER EATS"), (-360, "1 189.00"), (-540, "33 536.50"))
            .Line((110, "Closing Balance"), (-540, "33 536.50"))
            .Build();

        var result = _parser.Parse(file);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal([-245.50m, 25000m, -29m, -1189m], result.Transactions.Select(t => t.Amount));
        Assert.Equal(33536.50m, result.Transactions[^1].Balance);
        Assert.Equal("VODACOM AIRTIME R29.00", result.Transactions[2].Description);
        Assert.Equal(new DateOnly(2026, 3, 2), result.Transactions[0].Date);
        Assert.Equal(new BalanceCheck(4, 0), result.BalanceCheck);
        Assert.True(result.BalanceCheck!.AllGood);
    }

    [Fact]
    public void Reads_fnb_style_yearless_dates_and_cr_markers()
    {
        var file = new StatementPdf()
            .Line((40, "First National Bank"))
            .Line((40, "Statement Period : 01 March 2026 to 31 March 2026"))
            .Line((40, "Date"), (100, "Description"), (-430, "Amount"), (-530, "Balance"))
            .Line((100, "Opening Balance"), (-530, "10 000.00Cr"))
            .Line((40, "02 Mar"), (100, "POS PURCHASE ENGEN 4417"), (-430, "650.00"), (-530, "9 350.00Cr"))
            .Line((40, "25 Mar"), (100, "SALARY ACME"), (-430, "25 000.00Cr"), (-530, "34 350.00Cr"))
            .Line((40, "26 Mar"), (100, "#MONTHLY ACCOUNT FEE"), (-430, "69.00"), (-530, "34 281.00Cr"))
            .Build();

        var result = _parser.Parse(file);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal("FNB", result.BankName);
        Assert.Equal([-650m, 25000m, -69m], result.Transactions.Select(t => t.Amount));
        Assert.Equal([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 25), new DateOnly(2026, 3, 26)], result.Transactions.Select(t => t.Date));
        Assert.Equal(new BalanceCheck(3, 0), result.BalanceCheck);
    }

    [Fact]
    public void Works_without_a_header_newest_first_with_wrapped_descriptions()
    {
        var file = new StatementPdf()
            .Line((40, "14/03/2026"), (110, "CHECKERS HYPER"), (-430, "310.00"), (-530, "5 690.00"))
            .Line(12, (110, "SANDTON CITY"))
            .Line((40, "12/03/2026"), (110, "REFUND TAKEALOT"), (-430, "500.00"), (-530, "6 000.00"))
            .Line((40, "10/03/2026"), (110, "NETFLIX.COM"), (-430, "199.00"), (-530, "5 500.00"))
            .Build();

        var result = _parser.Parse(file);

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal(["NETFLIX.COM", "REFUND TAKEALOT", "CHECKERS HYPER SANDTON CITY"], result.Transactions.Select(t => t.Description));
        Assert.Equal([-199m, 500m, -310m], result.Transactions.Select(t => t.Amount));
        Assert.Equal(new BalanceCheck(2, 0), result.BalanceCheck);
    }

    [Fact]
    public void Continues_across_pages()
    {
        var file = new StatementPdf()
            .Line((40, "Date"), (110, "Details"), (-430, "Amount"), (-530, "Balance"))
            .Line((40, "2026/03/02"), (110, "SPAR"), (-430, "-100.00"), (-530, "900.00"))
            .NewPage()
            .Line((40, "Date"), (110, "Details"), (-430, "Amount"), (-530, "Balance"))
            .Line((40, "2026/03/03"), (110, "SHOPRITE"), (-430, "-50.00"), (-530, "850.00"))
            .Build();

        var result = _parser.Parse(file);

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal(new BalanceCheck(1, 0), result.BalanceCheck);
    }

    [Fact]
    public void Flags_rows_where_the_balance_does_not_add_up()
    {
        var file = new StatementPdf()
            .Line((40, "Date"), (110, "Description"), (-430, "Amount"), (-530, "Balance"))
            .Line((40, "2026/03/02"), (110, "SPAR"), (-430, "-100.00"), (-530, "900.00"))
            .Line((40, "2026/03/03"), (110, "MYSTERY"), (-430, "-50.00"), (-530, "700.00"))
            .Build();

        var result = _parser.Parse(file);

        Assert.Equal(new BalanceCheck(1, 1), result.BalanceCheck);
        Assert.Contains(result.Warnings, w => w.Contains("MYSTERY"));
    }

    [Fact]
    public void Scanned_pdfs_get_a_helpful_message()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);

        var result = _parser.Parse(new StatementFile("scan.pdf", builder.Build()));

        Assert.Equal(ParseStatus.Failed, result.Status);
        Assert.Equal(PdfStatementParser.ScannedMessage, result.Message);
    }

    [Fact]
    public void Only_claims_real_pdfs()
    {
        Assert.True(_parser.CanParse(new StatementPile().Build("whatever.bin")));
        Assert.False(_parser.CanParse(new StatementFile("fake.pdf", "Date,Amount"u8.ToArray())));
    }

    [Fact]
    public void December_entries_on_a_january_statement_get_last_year()
    {
        LayoutWord W(string text, double left) => new(text, left, left + text.Length * 5, 0, 9);
        var lines = new[]
        {
            new LayoutLine(1, 800, [W("Statement", 40), W("period", 90), W("01", 130), W("Jan", 145), W("2026", 165)]),
            new LayoutLine(1, 785, [W("30", 40), W("Dec", 55), W("SPAR", 100), W("80.00", 400), W("920.00", 480)]),
            new LayoutLine(1, 770, [W("02", 40), W("Jan", 55), W("PNP", 100), W("20.00", 400), W("900.00", 480)]),
        };

        var result = StatementTableReader.Read(lines);

        Assert.Equal([new DateOnly(2025, 12, 30), new DateOnly(2026, 1, 2)], result.Transactions.Select(t => t.Date));
        Assert.Equal(-20m, result.Transactions[1].Amount);
    }

    private sealed class StatementPile
    {
        public StatementFile Build(string name)
        {
            var builder = new PdfDocumentBuilder();
            builder.AddPage(PageSize.A4);
            return new StatementFile(name, builder.Build());
        }
    }
}
