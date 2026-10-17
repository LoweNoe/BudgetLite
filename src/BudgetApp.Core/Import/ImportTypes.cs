namespace BudgetApp.Core.Import;

// Password is only used to open a protected PDF in memory and is never stored.
public sealed record StatementFile(string FileName, byte[] Content, string? Password = null)
{
    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
    public bool LooksLikePdf => Content.AsSpan(0, Math.Min(5, Content.Length)).StartsWith("%PDF"u8);
}

public sealed record RawTransaction(
    DateOnly Date,
    string Description,
    decimal Amount,
    decimal? Balance,
    string? MerchantCategoryCode = null,
    int SourceRow = -1);

public sealed record ColumnMapping
{
    public char Delimiter { get; init; } = ',';

    // -1 when the file has no header row.
    public int HeaderRowIndex { get; init; } = -1;

    public int DateColumn { get; init; } = -1;
    public int DescriptionColumn { get; init; } = -1;
    public int? AmountColumn { get; init; }
    public int? DebitColumn { get; init; }
    public int? CreditColumn { get; init; }
    public int? BalanceColumn { get; init; }
    public int? MerchantCategoryCodeColumn { get; init; }

    public bool IsComplete =>
        DateColumn >= 0 && DescriptionColumn >= 0 &&
        (AmountColumn is not null || DebitColumn is not null || CreditColumn is not null);
}

public enum ParseStatus { Success, NeedsMapping, NeedsPassword, Failed }

// How many consecutive rows were checked against the statement's running balance, and how many didn't add up.
public sealed record BalanceCheck(int Checked, int Mismatches)
{
    public bool AllGood => Checked > 0 && Mismatches == 0;
}

public sealed record ParseResult
{
    public ParseStatus Status { get; init; }
    public string? BankName { get; init; }
    public BalanceCheck? BalanceCheck { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<RawTransaction> Transactions { get; init; } = [];
    public IReadOnlyList<string> Headers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; init; } = [];
    public ColumnMapping? Mapping { get; init; }
    public int SkippedRows { get; init; }
    public string? Message { get; init; }
    public string ParserName { get; init; } = "";

    public static ParseResult Failed(string message) => new() { Status = ParseStatus.Failed, Message = message };
}
