using System.Globalization;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BudgetApp.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

public sealed record CategoryOption(string Id, string Name, string Icon, string Colour)
{
    public static CategoryOption From(Category c) => new(c.Id, c.Name, c.Icon, c.Colour);
}

public sealed record WeekendOption(string Label, WeekendPayday Rule)
{
    public static readonly IReadOnlyList<WeekendOption> All =
    [
        new("Paid the Friday before", WeekendPayday.FridayBefore),
        new("Paid the Monday after", WeekendPayday.MondayAfter),
        new("Paid on the day", WeekendPayday.SameDay),
    ];
}

internal static class Display
{
    private static readonly NumberFormatInfo Counts = new() { NumberGroupSeparator = MoneyFormatter.Space.ToString(), NumberGroupSizes = [3] };

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Month(DateOnly date) => date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static string Count(int n, string singular, string? plural = null) =>
        $"{n.ToString("#,0", Counts)} {(n == 1 ? singular : plural ?? singular + "s")}";
}
