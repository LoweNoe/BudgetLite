using System.Text;
using BudgetApp.Core.Categorisation;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.Data;
using BudgetApp.Import;
using static BudgetApp.Tests.Core.AnalysisTestData;

namespace BudgetApp.Tests.Data;

public sealed class WorkspaceAndBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BudgetAppTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<BudgetWorkspace> LoadAsync()
    {
        var workspace = new BudgetWorkspace(new JsonDataStore(new DataPaths(_root)), new RuleBasedCategoriser());
        await workspace.LoadAsync();
        return workspace;
    }

    [Fact]
    public async Task Categories_can_be_added_and_deleted()
    {
        var workspace = await LoadAsync();
        var pets = await workspace.AddCategoryAsync("Pets & Vet", "heart", "chart-10", isEssential: true);
        Assert.Equal("pets-vet", pets.Id);

        var t = T("2025-03-01", -400, "VET", pets.Id);
        await workspace.AddTransactionsAsync([t]);
        await workspace.AddRuleAndApplyAsync(new CategoryRule { Pattern = "VET", CategoryId = pets.Id, Source = RuleSource.UserCorrection });

        Assert.Equal(1, await workspace.DeleteCategoryAsync(pets.Id));
        Assert.Equal(CategoryIds.Uncategorised, t.CategoryId);
        Assert.DoesNotContain(workspace.Rules, r => r.CategoryId == pets.Id);

        var reloaded = await LoadAsync();
        Assert.DoesNotContain(reloaded.Categories, c => c.Id == pets.Id);
    }

    [Fact]
    public async Task Protected_categories_cannot_be_deleted()
    {
        var workspace = await LoadAsync();

        Assert.Equal(0, await workspace.DeleteCategoryAsync(CategoryIds.Uncategorised));
        Assert.Contains(workspace.Categories, c => c.Id == CategoryIds.Uncategorised);
    }

    [Fact]
    public async Task Duplicate_category_names_get_unique_ids()
    {
        var workspace = await LoadAsync();

        Assert.Equal("groceries-2", (await workspace.AddCategoryAsync("Groceries", "cart", "chart-1", true)).Id);
    }

    [Fact]
    public async Task Reapplying_rules_only_touches_uncategorised_transactions()
    {
        var workspace = await LoadAsync();
        var manual = T("2025-03-01", -50, "SPAR", CategoryIds.Shopping);
        var loose = T("2025-03-02", -60, "SPAR", CategoryIds.Uncategorised);
        await workspace.AddTransactionsAsync([manual, loose]);

        Assert.Equal(1, await workspace.ReapplyRulesAsync());
        Assert.Equal(CategoryIds.Shopping, manual.CategoryId);
        Assert.Equal(CategoryIds.Groceries, loose.CategoryId);
    }

    [Fact]
    public async Task Removed_rules_can_be_restored()
    {
        var workspace = await LoadAsync();
        var rule = workspace.Rules.First(r => r.Pattern == "NETFLIX");

        await workspace.RemoveRuleAsync(rule);
        Assert.DoesNotContain(rule, workspace.Rules);
        await workspace.RestoreRuleAsync(rule);
        Assert.Contains(rule, workspace.Rules);
    }

    [Fact]
    public async Task Recurring_flags_are_set_on_import()
    {
        var workspace = await LoadAsync();
        var a = T("2025-02-05", -199, "NETFLIX.COM", CategoryIds.Subscriptions);
        var b = T("2025-03-05", -199, "NETFLIX.COM", CategoryIds.Subscriptions);
        var c = T("2025-03-06", -80, "SPAR", CategoryIds.Groceries);

        await workspace.AddTransactionsAsync([a, b, c]);

        Assert.True(a.IsRecurring);
        Assert.True(b.IsRecurring);
        Assert.False(c.IsRecurring);
    }

    [Fact]
    public async Task Version_one_data_is_migrated_to_the_new_palette()
    {
        var store = new JsonDataStore(new DataPaths(_root));
        var categories = BuiltInCatalog.CreateCategories();
        categories.Single(c => c.Id == CategoryIds.Transport).Colour = "chart-2";
        await store.SaveCategoriesAsync(categories);
        await store.SaveSettingsAsync(new AppSettings { DataVersion = 1 });

        var workspace = await LoadAsync();

        Assert.Equal("chart-9", workspace.FindCategory(CategoryIds.Transport)!.Colour);
        Assert.Equal(AppSettings.CurrentDataVersion, workspace.Settings.DataVersion);
    }

    [Fact]
    public async Task Backups_round_trip_and_keep_a_safety_copy()
    {
        var workspace = await LoadAsync();
        await workspace.AddTransactionsAsync([T("2025-03-01", -10, "SPAR", CategoryIds.Groceries)]);
        var backups = new BackupService(new DataPaths(_root));

        using var zip = new MemoryStream();
        await backups.CreateAsync(zip);

        await workspace.ClearTransactionsAsync();
        zip.Position = 0;
        var result = await backups.RestoreAsync(zip);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Single((await LoadAsync()).Transactions);
    }

    [Fact]
    public async Task Restoring_junk_changes_nothing()
    {
        var workspace = await LoadAsync();
        await workspace.AddTransactionsAsync([T("2025-03-01", -10, "SPAR", CategoryIds.Groceries)]);

        var result = await new BackupService(new DataPaths(_root)).RestoreAsync(new MemoryStream("not a zip"u8.ToArray()));

        Assert.False(result.Success);
        Assert.Single((await LoadAsync()).Transactions);
    }

    [Fact]
    public async Task Budgets_persist_and_follow_their_category()
    {
        var workspace = await LoadAsync();
        var pets = await workspace.AddCategoryAsync("Pets", "heart", "chart-10", isEssential: true);
        await workspace.SetBudgetAsync(pets.Id, 450);
        await workspace.SetBudgetAsync(CategoryIds.Takeaways, 1200);
        await workspace.SetBudgetAsync(CategoryIds.Takeaways, 1000);
        Assert.Equal(1, await workspace.AddBudgetsAsync([new CategoryBudget { CategoryId = CategoryIds.Groceries, Amount = 3000 }, new CategoryBudget { CategoryId = "missing", Amount = 1 }]));

        var reloaded = await LoadAsync();
        Assert.Equal(3, reloaded.Budgets.Count);
        Assert.Equal(1000m, reloaded.FindBudget(CategoryIds.Takeaways)!.Amount);

        await reloaded.DeleteCategoryAsync(pets.Id);
        Assert.Null(reloaded.FindBudget(pets.Id));

        var removed = await reloaded.RemoveBudgetAsync(CategoryIds.Groceries);
        Assert.Equal(3000m, removed!.Amount);
        Assert.Single((await LoadAsync()).Budgets);
    }

    [Fact]
    public async Task Exports_transactions_as_csv()
    {
        using var output = new MemoryStream();
        await TransactionCsvExporter.WriteAsync(output,
            [T("2025-03-01", -1234.5m, "PNP CPT", CategoryIds.Groceries, 99, raw: "POS PURCHASE PNP CPT")],
            BuiltInCatalog.CreateCategories());

        var lines = Encoding.UTF8.GetString(output.ToArray()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date,Description,Clean description,Amount,Balance,Category,Recurring,Notes", lines[0]);
        Assert.Equal("2025-03-01,POS PURCHASE PNP CPT,PNP CPT,-1234.50,99.00,Groceries,No,", lines[1]);
    }
}
