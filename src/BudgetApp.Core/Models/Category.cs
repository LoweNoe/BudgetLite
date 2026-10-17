namespace BudgetApp.Core.Models;

public sealed class Category
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // A palette token such as "chart-3", resolved by the UI per theme.
    public string Colour { get; set; } = "chart-1";

    public string Icon { get; set; } = "tag";
    public bool IsEssential { get; set; }
    public bool IsBuiltIn { get; set; }
}

public static class CategoryIds
{
    public const string Groceries = "groceries";
    public const string Takeaways = "takeaways";
    public const string Transport = "transport";
    public const string Fuel = "fuel";
    public const string Subscriptions = "subscriptions";
    public const string AirtimeData = "airtime-data";
    public const string Utilities = "utilities";
    public const string BankFees = "bank-fees";
    public const string Entertainment = "entertainment";
    public const string Shopping = "shopping";
    public const string Health = "health";
    public const string Transfers = "transfers";
    public const string Income = "income";
    public const string Uncategorised = "uncategorised";
}
