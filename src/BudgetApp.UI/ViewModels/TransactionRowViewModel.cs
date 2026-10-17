using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BudgetApp.UI.ViewModels;

public sealed partial class TransactionRowViewModel : ObservableObject
{
    private readonly Func<TransactionRowViewModel, CategoryOption, Task> _onCategoryChanged;
    private bool _syncing;

    public TransactionRowViewModel(
        Transaction transaction,
        IReadOnlyList<CategoryOption> categories,
        string currency,
        Func<TransactionRowViewModel, CategoryOption, Task> onCategoryChanged)
    {
        Transaction = transaction;
        Categories = categories;
        _onCategoryChanged = onCategoryChanged;
        AmountText = MoneyFormatter.Format(transaction.Amount, currency);
        SyncFromModel();
    }

    public Transaction Transaction { get; }
    public IReadOnlyList<CategoryOption> Categories { get; }
    public string DateText => Display.Date(Transaction.Date);
    public string Description => string.IsNullOrWhiteSpace(Transaction.CleanDescription) ? Transaction.Description : Transaction.CleanDescription;
    public string RawDescription => Transaction.Description;
    public string AmountText { get; }
    public bool IsCredit => Transaction.Amount > 0;
    public bool IsRecurring => Transaction.IsRecurring;

    [ObservableProperty]
    public partial CategoryOption? SelectedCategory { get; set; }

    public void SyncFromModel()
    {
        _syncing = true;
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == Transaction.CategoryId)
                           ?? Categories.FirstOrDefault(c => c.Id == CategoryIds.Uncategorised);
        _syncing = false;
    }

    partial void OnSelectedCategoryChanged(CategoryOption? value)
    {
        if (_syncing || value is null || value.Id == Transaction.CategoryId)
            return;

        _ = _onCategoryChanged(this, value);
    }
}
