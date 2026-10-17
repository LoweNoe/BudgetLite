using System.Text.Json;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Models;

namespace BudgetApp.Data;

// Uses bank-tips.json from the data folder when the user has edited one, otherwise the bundled copy.
public sealed class BankTipsProvider : IBankTipsProvider
{
    private const string ResourceName = "BudgetApp.Data.bank-tips.json";
    private readonly DataPaths _paths;
    private IReadOnlyList<BankTip>? _cache;

    public BankTipsProvider(DataPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<BankTip>> GetTipsAsync(CancellationToken ct = default)
    {
        if (_cache is not null)
            return _cache;

        if (File.Exists(_paths.BankTips))
        {
            try
            {
                await using var file = File.OpenRead(_paths.BankTips);
                _cache = await ReadAsync(file, ct);
                if (_cache.Count > 0)
                    return _cache;
            }
            catch (JsonException)
            {
            }
        }

        await using var bundled = typeof(BankTipsProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        _cache = await ReadAsync(bundled, ct);
        return _cache;
    }

    private static async Task<IReadOnlyList<BankTip>> ReadAsync(Stream stream, CancellationToken ct)
    {
        var document = await JsonSerializer.DeserializeAsync<VersionedDocument<List<BankTip>>>(stream, JsonDataStore.Options, ct);
        return document?.Data ?? [];
    }
}
