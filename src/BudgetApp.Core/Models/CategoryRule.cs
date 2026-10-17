namespace BudgetApp.Core.Models;

public enum RuleSource { BuiltIn, UserCorrection }

public enum RuleMatchType
{
    Contains,
    Regex,
    // Pattern is a merchant category code ("5411") or an inclusive range ("5811-5814").
    MerchantCategoryCode
}

public enum AmountDirection { Any, Debits, Credits }

public sealed class CategoryRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Pattern { get; set; } = "";
    public RuleMatchType MatchType { get; set; } = RuleMatchType.Contains;
    public string CategoryId { get; set; } = CategoryIds.Uncategorised;
    public int Priority { get; set; }
    public RuleSource Source { get; set; } = RuleSource.BuiltIn;
    public AmountDirection AppliesTo { get; set; } = AmountDirection.Any;
}
