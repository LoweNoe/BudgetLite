using BudgetApp.Core.Models;

namespace BudgetApp.Tests.Core;

internal static class AnalysisTestData
{
    public static Transaction T(string date, decimal amount, string description, string category, decimal? balance = null, string? raw = null) => new()
    {
        Date = DateOnly.Parse(date),
        Amount = amount,
        Description = raw ?? description,
        CleanDescription = description,
        CategoryId = category,
        Balance = balance,
        ImportHash = Guid.NewGuid().ToString("N"),
    };

    public static List<Category> Categories() => BudgetApp.Core.Categorisation.BuiltInCatalog.CreateCategories();
}
