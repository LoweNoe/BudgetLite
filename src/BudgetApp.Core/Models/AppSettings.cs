namespace BudgetApp.Core.Models;

public enum ThemePreference { System, Light, Dark }

// What happens when payday lands on a weekend. Most South African employers pay on the Friday before.
public enum WeekendPayday { FridayBefore, MondayAfter, SameDay }

public sealed class AppSettings
{
    public const int CurrentDataVersion = 2;

    public int PayDay { get; set; } = 25;
    public WeekendPayday WeekendPayday { get; set; } = WeekendPayday.FridayBefore;
    public string Currency { get; set; } = "ZAR";
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool OnboardingCompleted { get; set; }
    public HashSet<string> DismissedTips { get; set; } = new(StringComparer.Ordinal);
    public int DataVersion { get; set; } = CurrentDataVersion;

    // Column mappings the user confirmed for CSV layouts we couldn't detect, keyed by the file's header row.
    public Dictionary<string, BudgetApp.Core.Import.ColumnMapping> SavedColumnMappings { get; set; } = new(StringComparer.Ordinal);
}
