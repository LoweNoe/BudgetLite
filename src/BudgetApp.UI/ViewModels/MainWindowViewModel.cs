using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace BudgetApp.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;

    public MainWindowViewModel(IServiceProvider services, BudgetWorkspace workspace, Navigator navigator, ToastService toasts, DialogService dialogs)
    {
        _services = services;
        Toasts = toasts;
        Dialogs = dialogs;
        navigator.OnboardingRequested += ShowOnboarding;

        if (workspace.Settings.OnboardingCompleted)
            ShowShell(Page.Dashboard);
        else
            ShowOnboarding();
    }

    public ToastService Toasts { get; }
    public DialogService Dialogs { get; }

    [ObservableProperty]
    public partial ViewModelBase? Content { get; set; }

    private void ShowOnboarding()
    {
        var onboarding = _services.GetRequiredService<OnboardingViewModel>();
        onboarding.Finished += (_, page) => ShowShell(page);
        Content = onboarding;
    }

    private void ShowShell(Page page)
    {
        var shell = _services.GetRequiredService<ShellViewModel>();
        shell.Navigate(page);
        Content = shell;
    }
}
