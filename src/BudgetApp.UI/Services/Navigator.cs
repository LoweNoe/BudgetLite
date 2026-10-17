using BudgetApp.Core.Analysis;

namespace BudgetApp.UI.Services;

public enum Page { Dashboard, Insights, Transactions, Budgets, Import, Categories, Settings }

public sealed record TransactionFilter(string? CategoryId = null, YearMonth? Month = null, string? Search = null);

public sealed class Navigator
{
    public event Action<Page>? PageRequested;
    public event Action? OnboardingRequested;
    public event Action<TransactionFilter>? TransactionFilterRequested;

    public void GoTo(Page page) => PageRequested?.Invoke(page);

    public void ShowOnboarding() => OnboardingRequested?.Invoke();

    // Jumps to Transactions with a filter already applied, e.g. from a dashboard chart.
    public void ShowTransactions(TransactionFilter filter)
    {
        TransactionFilterRequested?.Invoke(filter);
        PageRequested?.Invoke(Page.Transactions);
    }
}
