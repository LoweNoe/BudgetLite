namespace BudgetApp.Core.Models;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Bank { get; set; } = "";
}
