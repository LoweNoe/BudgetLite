using System.Globalization;
using System.Text;
using BudgetApp.Core.Models;
using CsvHelper;

namespace BudgetApp.Import;

public static class TransactionCsvExporter
{
    public static async Task WriteAsync(Stream output, IEnumerable<Transaction> transactions, IEnumerable<Category> categories, CancellationToken ct = default)
    {
        var names = categories.ToDictionary(c => c.Id, c => c.Name);

        await using var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true);
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        foreach (var header in new[] { "Date", "Description", "Clean description", "Amount", "Balance", "Category", "Recurring", "Notes" })
            csv.WriteField(header);
        await csv.NextRecordAsync();

        foreach (var t in transactions.OrderBy(t => t.Date))
        {
            ct.ThrowIfCancellationRequested();
            csv.WriteField(t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.WriteField(t.Description);
            csv.WriteField(t.CleanDescription);
            csv.WriteField(t.Amount.ToString("0.00", CultureInfo.InvariantCulture));
            csv.WriteField(t.Balance?.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(names.GetValueOrDefault(t.CategoryId, t.CategoryId));
            csv.WriteField(t.IsRecurring ? "Yes" : "No");
            csv.WriteField(t.Notes ?? "");
            await csv.NextRecordAsync();
        }

        await csv.FlushAsync();
    }
}
