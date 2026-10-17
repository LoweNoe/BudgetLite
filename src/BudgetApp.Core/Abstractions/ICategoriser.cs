using BudgetApp.Core.Models;

namespace BudgetApp.Core.Abstractions;

public sealed record CategorisationInput(string CleanDescription, string RawDescription, decimal Amount, string? MerchantCategoryCode = null)
{
    public static CategorisationInput From(Transaction t) =>
        new(t.CleanDescription, t.Description, t.Amount, t.MerchantCategoryCode);
}

public sealed record CategoryMatch(string CategoryId, CategoryRule? Rule)
{
    public bool IsFallback => Rule is null;
}

// Extension point for an ML.NET text classifier later on.
public interface ICategoriser
{
    CategoryMatch Categorise(CategorisationInput input);
}
