using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Import;
using BudgetApp.Import;
using BudgetApp.Import.Csv;

namespace BudgetApp.Tests.Import;

public class DuplicateDetectionTests
{
    private static readonly DateOnly Day = new(2025, 3, 1);

    [Fact]
    public void Same_inputs_give_the_same_hash()
    {
        Assert.Equal(
            ImportHasher.Compute(Day, -45.00m, "VIDA E CAFFE", 100m),
            ImportHasher.Compute(Day, -45m, "  vida   e caffe ", 100.00m));
    }

    [Theory]
    [InlineData(-45.01, "VIDA E CAFFE", 100.0)]
    [InlineData(-45.00, "VIDA E CAFFE ROSEBANK", 100.0)]
    [InlineData(-45.00, "VIDA E CAFFE", 55.0)]
    public void Any_field_change_gives_a_different_hash(double amount, string description, double balance)
    {
        Assert.NotEqual(
            ImportHasher.Compute(Day, -45.00m, "VIDA E CAFFE", 100m),
            ImportHasher.Compute(Day, (decimal)amount, description, (decimal)balance));
    }

    [Fact]
    public void Identical_rows_in_one_statement_are_kept_apart()
    {
        var coffee = new RawTransaction(Day, "VIDA E CAFFE", -45m, null);
        var hashes = ImportHasher.ComputeBatch([coffee, coffee]);

        Assert.Equal(2, hashes.Distinct().Count());
    }

    [Fact]
    public void Overlapping_statements_only_import_new_rows()
    {
        var importer = new StatementImporter([new CsvStatementParser()]);
        var categoriser = new RuleBasedCategoriser();
        var accountId = Guid.NewGuid();

        var february = importer.Parse(new StatementFile("feb.csv", """
            Date,Description,Amount
            2025/02/27,VIDA E CAFFE,-45.00
            2025/02/27,VIDA E CAFFE,-45.00
            2025/02/28,SPAR,-200.00
            """u8.ToArray()));
        var firstPreview = importer.BuildPreview(february.Transactions, _ => false, categoriser, accountId);
        var known = firstPreview.NewTransactions.Select(t => t.ImportHash).ToHashSet();

        var overlap = importer.Parse(new StatementFile("mar.csv", """
            Date,Description,Amount
            2025/02/27,VIDA E CAFFE,-45.00
            2025/02/27,VIDA E CAFFE,-45.00
            2025/02/28,SPAR,-200.00
            2025/03/01,CHECKERS,-310.00
            """u8.ToArray()));
        var secondPreview = importer.BuildPreview(overlap.Transactions, known.Contains, categoriser, accountId);

        Assert.Equal(3, firstPreview.NewCount);
        Assert.Equal(3, secondPreview.DuplicateCount);
        Assert.Equal("CHECKERS", Assert.Single(secondPreview.NewTransactions).Description);
    }
}
