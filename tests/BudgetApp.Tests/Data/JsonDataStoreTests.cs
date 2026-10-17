using System.Text.Json;
using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.Data;

namespace BudgetApp.Tests.Data;

public sealed class JsonDataStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BudgetAppTests", Guid.NewGuid().ToString("N"));
    private readonly JsonDataStore _store;

    public JsonDataStoreTests()
    {
        _store = new JsonDataStore(new DataPaths(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Round_trips_transactions_and_settings()
    {
        var transaction = new Transaction { Date = new DateOnly(2025, 3, 1), Description = "SPAR", Amount = -80.25m, Balance = null };
        var settings = new AppSettings { PayDay = 15, Theme = ThemePreference.Dark, DismissedTips = ["category-edit"] };

        await _store.SaveTransactionsAsync([transaction]);
        await _store.SaveSettingsAsync(settings);
        var loaded = await new JsonDataStore(new DataPaths(_root)).LoadAsync();

        var t = Assert.Single(loaded.Transactions);
        Assert.Equal(transaction.Id, t.Id);
        Assert.Equal(-80.25m, t.Amount);
        Assert.Null(t.Balance);
        Assert.Equal(15, loaded.Settings.PayDay);
        Assert.Equal(ThemePreference.Dark, loaded.Settings.Theme);
        Assert.Contains("category-edit", loaded.Settings.DismissedTips);
    }

    [Fact]
    public async Task Every_file_carries_a_version_and_no_temp_file_is_left()
    {
        await _store.SaveRulesAsync([new CategoryRule { Pattern = "SPAR" }]);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "rules.json")));
        Assert.Equal(JsonDataStore.CurrentFileVersion, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Corrupt_files_are_set_aside_instead_of_crashing()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "transactions.json"), "{ not json");

        var loaded = await _store.LoadAsync();

        Assert.Empty(loaded.Transactions);
        Assert.Single(Directory.GetFiles(_root, "transactions.json.corrupt-*"));
    }

    [Fact]
    public async Task Workspace_seeds_defaults_and_skips_duplicate_imports()
    {
        var workspace = new BudgetWorkspace(_store, new RuleBasedCategoriser());
        await workspace.LoadAsync();

        Assert.Contains(workspace.Categories, c => c.Id == CategoryIds.Groceries);
        Assert.NotEmpty(workspace.Rules);
        Assert.Single(workspace.Accounts);

        var t = new Transaction { ImportHash = "abc", Amount = -1 };
        Assert.Equal(1, await workspace.AddTransactionsAsync([t]));
        Assert.Equal(0, await workspace.AddTransactionsAsync([new Transaction { ImportHash = "abc" }]));
    }

    [Fact]
    public async Task Correction_rules_recategorise_matching_past_transactions()
    {
        var workspace = new BudgetWorkspace(_store, new RuleBasedCategoriser());
        await workspace.LoadAsync();
        var a = new Transaction { ImportHash = "a", Description = "POS PURCHASE GYM CO *1", CleanDescription = "GYM CO", Amount = -300 };
        var b = new Transaction { ImportHash = "b", Description = "GYM CO", CleanDescription = "GYM CO", Amount = -300 };
        await workspace.AddTransactionsAsync([a, b]);

        var rule = workspace.CreateCorrectionRule(a, CategoryIds.Health);
        Assert.Equal(2, workspace.CountAffectedBy(rule));
        Assert.Equal(2, await workspace.AddRuleAndApplyAsync(rule));

        var reloaded = new BudgetWorkspace(new JsonDataStore(new DataPaths(_root)), new RuleBasedCategoriser());
        await reloaded.LoadAsync();
        Assert.All(reloaded.Transactions, t => Assert.Equal(CategoryIds.Health, t.CategoryId));
        Assert.Contains(reloaded.Rules, r => r.Source == RuleSource.UserCorrection && r.Pattern == "GYM CO");
    }
}
