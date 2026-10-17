namespace BudgetApp.Data;

public sealed class DataPaths
{
    public DataPaths(string root)
    {
        Root = root;
    }

    public static DataPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BudgetApp"));

    public string Root { get; }
    public string Transactions => Path.Combine(Root, "transactions.json");
    public string Categories => Path.Combine(Root, "categories.json");
    public string Rules => Path.Combine(Root, "rules.json");
    public string Accounts => Path.Combine(Root, "accounts.json");
    public string Settings => Path.Combine(Root, "settings.json");
    public string Budgets => Path.Combine(Root, "budgets.json");
    public string BankTips => Path.Combine(Root, "bank-tips.json");
}
