namespace BudgetApp.Core.Models;

public sealed class BudgetData
{
    public List<Transaction> Transactions { get; init; } = [];
    public List<Category> Categories { get; init; } = [];
    public List<CategoryRule> Rules { get; init; } = [];
    public List<Account> Accounts { get; init; } = [];
    public List<CategoryBudget> Budgets { get; init; } = [];
    public AppSettings Settings { get; init; } = new();
}

public sealed class BankTip
{
    public string Bank { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Steps { get; set; } = [];
}
