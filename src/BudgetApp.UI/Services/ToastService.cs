using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.Services;

public enum ToastKind { Success, Info, Warning }

public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Func<Task>? _action;
    private readonly Action<ToastViewModel> _dismiss;

    public ToastViewModel(string message, ToastKind kind, string? actionText, Func<Task>? action, Action<ToastViewModel> dismiss)
    {
        Message = message;
        Kind = kind;
        ActionText = actionText;
        _action = action;
        _dismiss = dismiss;
    }

    public string Message { get; }
    public ToastKind Kind { get; }
    public string? ActionText { get; }
    public bool HasAction => ActionText is not null;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsInfo => Kind == ToastKind.Info;

    public string Icon => Kind switch
    {
        ToastKind.Success => "check-circle",
        ToastKind.Warning => "alert",
        _ => "info",
    };

    [ObservableProperty] public partial bool IsClosing { get; set; }

    [RelayCommand]
    private async Task RunAction()
    {
        _dismiss(this);
        if (_action is not null)
            await _action();
    }

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

public sealed class ToastService
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(220);

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Success, string? actionText = null, Func<Task>? action = null)
    {
        var toast = new ToastViewModel(message, kind, actionText, action, t => _ = CloseAsync(t));
        Toasts.Add(toast);

        while (Toasts.Count(t => !t.IsClosing) > MaxVisible)
            _ = CloseAsync(Toasts.First(t => !t.IsClosing));

        var lifetime = TimeSpan.FromSeconds(action is null ? 4 : 7);
        _ = CloseAfterAsync(toast, lifetime);
    }

    private async Task CloseAfterAsync(ToastViewModel toast, TimeSpan delay)
    {
        await Task.Delay(delay);
        await CloseAsync(toast);
    }

    // Flags the toast so its exit animation plays, then removes it once that's finished.
    private async Task CloseAsync(ToastViewModel toast)
    {
        if (toast.IsClosing)
            return;

        toast.IsClosing = true;
        await Task.Delay(ExitDuration);
        Toasts.Remove(toast);
    }
}
