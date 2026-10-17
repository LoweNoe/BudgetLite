namespace BudgetApp.Core.Models;

public sealed class CategoryBudget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CategoryId { get; set; } = "";
    public decimal Amount { get; set; }
}
