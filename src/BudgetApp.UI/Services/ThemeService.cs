using Avalonia;
using Avalonia.Styling;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;

namespace BudgetApp.UI.Services;

public sealed class ThemeService
{
    private readonly BudgetWorkspace _workspace;
    private Application? _app;

    public ThemeService(BudgetWorkspace workspace)
    {
        _workspace = workspace;
    }

    // Raised just before a user-initiated switch so the window can snapshot itself and cross-fade.
    public event EventHandler? ThemeChanging;

    // Fires when the effective theme flips, including OS changes while following the system.
    public event EventHandler? ThemeChanged;
    public event EventHandler? PreferenceChanged;

    public ThemePreference Preference => _workspace.Settings.Theme;

    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public void ApplyCurrent() => Apply(Preference);

    public async Task SetAsync(ThemePreference preference)
    {
        if (preference == _workspace.Settings.Theme)
            return;

        var wasDark = IsDark;
        _workspace.Settings.Theme = preference;

        if (WouldBeDark(preference) != wasDark)
            ThemeChanging?.Invoke(this, EventArgs.Empty);

        Apply(preference);
        PreferenceChanged?.Invoke(this, EventArgs.Empty);
        await _workspace.SaveSettingsAsync();
    }

    private bool WouldBeDark(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark,
    };

    private void Apply(ThemePreference preference)
    {
        var app = Application.Current;
        if (app is null)
            return;

        if (_app != app)
        {
            _app = app;
            app.ActualThemeVariantChanged += (_, _) => ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        app.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
