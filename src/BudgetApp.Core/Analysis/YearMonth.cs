using System.Globalization;

namespace BudgetApp.Core.Analysis;

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public DateOnly First => new(Year, Month, 1);
    public DateOnly Last => new(Year, Month, DaysInMonth);
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public YearMonth AddMonths(int months) => From(First.AddMonths(months));

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public int CompareTo(YearMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public static bool operator <(YearMonth a, YearMonth b) => a.CompareTo(b) < 0;
    public static bool operator >(YearMonth a, YearMonth b) => a.CompareTo(b) > 0;
    public static bool operator <=(YearMonth a, YearMonth b) => a.CompareTo(b) <= 0;
    public static bool operator >=(YearMonth a, YearMonth b) => a.CompareTo(b) >= 0;

    public string ShortName => First.ToString("MMM", CultureInfo.InvariantCulture);

    public override string ToString() => First.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
