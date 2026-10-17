namespace BudgetApp.Core.Models;

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public string Description { get; set; } = "";
    public string CleanDescription { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public string CategoryId { get; set; } = CategoryIds.Uncategorised;
    public Guid AccountId { get; set; }
    public string ImportHash { get; set; } = "";
    public bool IsRecurring { get; set; }
    public string? Notes { get; set; }
    public string? MerchantCategoryCode { get; set; }
}
