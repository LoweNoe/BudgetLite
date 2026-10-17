using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.Services;

public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public ConfirmDialogViewModel(string title, string message, string confirmText, string cancelText, bool isDestructive)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDestructive = isDestructive;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }
    public bool IsDestructive { get; }
    public bool IsNormal => !IsDestructive;

    public Task<bool> Result => _result.Task;

    [RelayCommand]
    private void Confirm() => _result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);
}

// In-app modal confirmation, rendered as an overlay by the main window.
public sealed partial class DialogService : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    public partial ConfirmDialogViewModel? Current { get; set; }

    public bool HasDialog => Current is not null;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false, string cancelText = "Cancel")
    {
        Current?.CancelCommand.Execute(null);

        var dialog = new ConfirmDialogViewModel(title, message, confirmText, cancelText, destructive);
        Current = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            if (Current == dialog)
                Current = null;
        }
    }
}
