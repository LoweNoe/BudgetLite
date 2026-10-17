using System.Text.Json;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Models;

namespace BudgetApp.Data;

public sealed class JsonDataStore : IDataStore
{
    public const int CurrentFileVersion = 1;

    internal static JsonSerializerOptions Options => DataJsonContext.Default.Options;

    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public JsonDataStore(DataPaths paths)
    {
        Paths = paths;
    }

    public DataPaths Paths { get; }
    public string Location => Paths.Root;

    public async Task<BudgetData> LoadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Paths.Root);

        return new BudgetData
        {
            Transactions = await ReadAsync<List<Transaction>>(Paths.Transactions, ct) ?? [],
            Categories = await ReadAsync<List<Category>>(Paths.Categories, ct) ?? [],
            Rules = await ReadAsync<List<CategoryRule>>(Paths.Rules, ct) ?? [],
            Accounts = await ReadAsync<List<Account>>(Paths.Accounts, ct) ?? [],
            Settings = await ReadAsync<AppSettings>(Paths.Settings, ct) ?? new AppSettings(),
            Budgets = await ReadAsync<List<CategoryBudget>>(Paths.Budgets, ct) ?? [],
        };
    }

    public Task SaveBudgetsAsync(IReadOnlyList<CategoryBudget> budgets, CancellationToken ct = default) =>
        WriteAsync(Paths.Budgets, budgets.ToList(), ct);

    public Task SaveTransactionsAsync(IReadOnlyList<Transaction> transactions, CancellationToken ct = default) =>
        WriteAsync(Paths.Transactions, transactions.ToList(), ct);

    public Task SaveCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct = default) =>
        WriteAsync(Paths.Categories, categories.ToList(), ct);

    public Task SaveRulesAsync(IReadOnlyList<CategoryRule> rules, CancellationToken ct = default) =>
        WriteAsync(Paths.Rules, rules.ToList(), ct);

    public Task SaveAccountsAsync(IReadOnlyList<Account> accounts, CancellationToken ct = default) =>
        WriteAsync(Paths.Accounts, accounts.ToList(), ct);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default) =>
        WriteAsync(Paths.Settings, settings, ct);

    private async Task<T?> ReadAsync<T>(string path, CancellationToken ct) where T : class
    {
        if (!File.Exists(path))
            return null;

        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<VersionedDocument<T>>(stream, Options, ct);
            return document is null ? null : Migrations.Upgrade(document).Data;
        }
        catch (JsonException)
        {
            // Keep the unreadable file for recovery rather than silently overwriting it later.
            QuarantineCorruptFile(path);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task WriteAsync<T>(string path, T data, CancellationToken ct)
    {
        var gate = LockFor(path);
        await gate.WaitAsync(ct);
        try
        {
            var document = new VersionedDocument<T> { Version = CurrentFileVersion, Data = data };
            await AtomicFile.WriteAsync(path, stream => JsonSerializer.SerializeAsync(stream, document, Options, ct), ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim LockFor(string path)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(path, out var gate))
                _locks[path] = gate = new SemaphoreSlim(1, 1);
            return gate;
        }
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
