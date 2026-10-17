using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Import;
using BudgetApp.Core.Models;

namespace BudgetApp.Import;

public sealed record ImportCandidate(Transaction Transaction, bool IsDuplicate);

public sealed class ImportPreview
{
    public ImportPreview(IReadOnlyList<ImportCandidate> items)
    {
        Items = items;
    }

    public IReadOnlyList<ImportCandidate> Items { get; }
    public IEnumerable<Transaction> NewTransactions => Items.Where(i => !i.IsDuplicate).Select(i => i.Transaction);
    public int NewCount => Items.Count(i => !i.IsDuplicate);
    public int DuplicateCount => Items.Count(i => i.IsDuplicate);
    public DateOnly? From => Items.Count > 0 ? Items.Min(i => i.Transaction.Date) : null;
    public DateOnly? To => Items.Count > 0 ? Items.Max(i => i.Transaction.Date) : null;
}

public sealed class StatementImporter
{
    private readonly IReadOnlyList<IStatementParser> _parsers;

    public StatementImporter(IEnumerable<IStatementParser> parsers)
    {
        _parsers = parsers.OrderByDescending(p => p.Specificity).ToList();
    }

    public ParseResult Parse(StatementFile file, ColumnMapping? mapping = null)
    {
        var parser = _parsers.FirstOrDefault(p => p.CanParse(file));
        if (parser is null)
            return ParseResult.Failed("This file type isn't supported yet. Export your statement as a CSV file and try again.");

        return parser.Parse(file, mapping);
    }

    public ImportPreview BuildPreview(IReadOnlyList<RawTransaction> rows, Func<string, bool> isKnownHash, ICategoriser categoriser, Guid accountId)
    {
        var hashes = ImportHasher.ComputeBatch(rows);
        var items = new List<ImportCandidate>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var transaction = new Transaction
            {
                Date = row.Date,
                Description = row.Description,
                CleanDescription = DescriptionCleaner.Clean(row.Description),
                Amount = row.Amount,
                Balance = row.Balance,
                AccountId = accountId,
                ImportHash = hashes[i],
                MerchantCategoryCode = row.MerchantCategoryCode,
            };
            transaction.CategoryId = categoriser.Categorise(CategorisationInput.From(transaction)).CategoryId;
            items.Add(new ImportCandidate(transaction, isKnownHash(hashes[i])));
        }

        return new ImportPreview(items);
    }
}
