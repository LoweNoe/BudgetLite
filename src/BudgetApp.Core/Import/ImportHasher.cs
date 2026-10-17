using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BudgetApp.Core.Import;

public static class ImportHasher
{
    public static string Compute(DateOnly date, decimal amount, string rawDescription, decimal? balance)
    {
        var inv = CultureInfo.InvariantCulture;
        var key = string.Join('|',
            date.ToString("yyyy-MM-dd", inv),
            amount.ToString("0.00", inv),
            NormaliseDescription(rawDescription),
            balance?.ToString("0.00", inv) ?? "");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    // Identical rows within one statement (two coffees on the same day, no balance column) are
    // real transactions, so repeats get an occurrence suffix. Re-importing the same or an
    // overlapping statement produces the same suffixes, so duplicates are still caught.
    public static IReadOnlyList<string> ComputeBatch(IEnumerable<RawTransaction> rows)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var hashes = new List<string>();

        foreach (var row in rows)
        {
            var hash = Compute(row.Date, row.Amount, row.Description, row.Balance);
            seen.TryGetValue(hash, out var count);
            seen[hash] = count + 1;
            hashes.Add(count == 0 ? hash : $"{hash}-{count}");
        }

        return hashes;
    }

    private static string NormaliseDescription(string description) =>
        string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
