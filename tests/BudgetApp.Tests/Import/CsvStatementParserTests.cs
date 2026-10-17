using System.Text;
using BudgetApp.Core.Import;
using BudgetApp.Import.Csv;

namespace BudgetApp.Tests.Import;

public class CsvStatementParserTests
{
    private readonly CsvStatementParser _parser = new();

    private static StatementFile Csv(string content, string name = "statement.csv") =>
        new(name, Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")));

    [Fact]
    public void Reads_a_simple_file_with_an_amount_column()
    {
        var result = _parser.Parse(Csv("""
            Date,Description,Amount,Balance
            2025/03/01,POS PURCHASE PNP CPT 1234 *4417,-245.50,10254.50
            2025/03/02,SALARY ACME LTD,25000.00,35254.50
            """));

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal(2, result.Transactions.Count);

        var first = result.Transactions[0];
        Assert.Equal(new DateOnly(2025, 3, 1), first.Date);
        Assert.Equal(-245.50m, first.Amount);
        Assert.Equal(10254.50m, first.Balance);
        Assert.Equal("POS PURCHASE PNP CPT 1234 *4417", first.Description);
    }

    [Fact]
    public void Combines_separate_debit_and_credit_columns()
    {
        var result = _parser.Parse(Csv("""
            Transaction Date,Details,Debit,Credit,Balance
            01/03/2025,NETFLIX,199.00,,5000.00
            02/03/2025,REFUND TAKEALOT,,350.00,5350.00
            """));

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal([-199.00m, 350.00m], result.Transactions.Select(t => t.Amount));
    }

    [Fact]
    public void Skips_preamble_lines_before_the_header()
    {
        var result = _parser.Parse(Csv("""
            ACCOUNT HISTORY
            Account Number,62000000000
            Statement Period,01 Mar 2025 to 31 Mar 2025

            Date,Amount,Balance,Description
            2025/03/01,-50.00,950.00,UBER TRIP
            2025/03/03,-120.00,830.00,MR D FOOD
            """));

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("MR D FOOD", result.Transactions[1].Description);
    }

    [Fact]
    public void Handles_semicolons_quotes_and_comma_decimals()
    {
        var result = _parser.Parse(Csv("""
            Datum;Description;Amount
            14/03/2025;"WOOLWORTHS; SANDTON";-1 234,56
            """));

        Assert.Equal(ParseStatus.Success, result.Status);
        var t = Assert.Single(result.Transactions);
        Assert.Equal("WOOLWORTHS; SANDTON", t.Description);
        Assert.Equal(-1234.56m, t.Amount);
    }

    [Fact]
    public void Ignores_opening_and_closing_balance_rows()
    {
        var result = _parser.Parse(Csv("""
            Date,Description,Amount,Balance
            2025/03/01,Opening Balance,0.00,1000.00
            2025/03/02,SPAR,-80.00,920.00
            2025/03/31,Closing Balance,0.00,920.00
            """));

        Assert.Single(result.Transactions);
    }

    [Fact]
    public void Infers_columns_when_there_is_no_header()
    {
        var result = _parser.Parse(Csv("""
            2025/03/01,-245.50,10254.50,PNP CPT
            2025/03/02,-99.00,10155.50,SPOTIFY
            2025/03/03,-500.00,9655.50,ENGEN BRYANSTON
            """));

        Assert.Equal(ParseStatus.Success, result.Status);
        Assert.Equal(3, result.Transactions.Count);
        Assert.Equal(-99.00m, result.Transactions[1].Amount);
        Assert.Equal(10155.50m, result.Transactions[1].Balance);
        Assert.Equal("SPOTIFY", result.Transactions[1].Description);
    }

    [Fact]
    public void Asks_for_a_mapping_when_columns_cannot_be_detected()
    {
        var result = _parser.Parse(Csv("""
            When,What,How much
            yesterday,coffee,lots
            today,lunch,more
            """));

        Assert.Equal(ParseStatus.NeedsMapping, result.Status);
        Assert.Equal(["When", "What", "How much"], result.Headers);
        Assert.Equal(0, result.Mapping!.HeaderRowIndex);
    }

    [Fact]
    public void Uses_a_user_supplied_mapping()
    {
        var mapping = new ColumnMapping { Delimiter = ',', HeaderRowIndex = 0, DateColumn = 2, DescriptionColumn = 0, AmountColumn = 1 };
        var result = _parser.Parse(Csv("""
            Merchant,Value (ZAR),Posted
            CHECKERS,-300.00,2025-03-05
            """), mapping);

        var t = Assert.Single(result.Transactions);
        Assert.Equal("CHECKERS", t.Description);
        Assert.Equal(new DateOnly(2025, 3, 5), t.Date);
    }

    [Fact]
    public void Picks_up_a_merchant_category_code_column()
    {
        var result = _parser.Parse(Csv("""
            Date,Description,Amount,MCC
            2025/03/01,CORNER CAFE,-45.00,5814
            """));

        Assert.Equal("5814", Assert.Single(result.Transactions).MerchantCategoryCode);
    }

    [Fact]
    public void Does_not_claim_pdf_files()
    {
        Assert.False(_parser.CanParse(new StatementFile("statement.pdf", "%PDF-1.7"u8.ToArray())));
        Assert.False(_parser.CanParse(new StatementFile("statement.csv", "%PDF-1.7"u8.ToArray())));
        Assert.True(_parser.CanParse(Csv("Date,Amount")));
    }
}
