using BudgetApp.Core.Models;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BudgetApp.UI.ViewModels;

// Backs the Light / Dark / Follow system radio group used in onboarding and settings.
public sealed class ThemeChoiceViewModel : ObservableObject
{
    private readonly ThemeService _theme;

    public ThemeChoiceViewModel(ThemeService theme)
    {
        _theme = theme;
        theme.PreferenceChanged += (_, _) => RaiseAll();
    }

    public bool IsLight
    {
        get => _theme.Preference == ThemePreference.Light;
        set => Choose(value, ThemePreference.Light);
    }

    public bool IsDark
    {
        get => _theme.Preference == ThemePreference.Dark;
        set => Choose(value, ThemePreference.Dark);
    }

    public bool IsSystem
    {
        get => _theme.Preference == ThemePreference.System;
        set => Choose(value, ThemePreference.System);
    }

    private void Choose(bool selected, ThemePreference preference)
    {
        // Radio groups also push "false" to the options being unchecked; only act on the new choice.
        if (selected)
            _ = _theme.SetAsync(preference);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(IsSystem));
    }
}
