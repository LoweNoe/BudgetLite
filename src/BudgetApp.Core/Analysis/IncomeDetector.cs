using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public static class IncomeDetector
{
    private const decimal AmountTolerance = 0.15m;

    public static IReadOnlyList<IncomeSource> Detect(IReadOnlyList<Transaction> transactions, DateOnly asOf, WeekendPayday fallbackRule)
    {
        var sources = new List<IncomeSource>();

        var groups = transactions
            .Where(t => t.Amount > 0 && t.CategoryId == CategoryIds.Income)
            .GroupBy(RecurringDetector.MerchantKey)
            .Where(g => g.Key.Length > 0 && g.Count() >= 2);

        foreach (var group in groups)
        {
            var items = group.OrderBy(t => t.Date).ToList();
            if (RecurringDetector.MatchCadence(items.Select(t => t.Date).ToList()) is not { } cadence)
                continue;
            if (!RecurringDetector.AmountsAgree(items.Select(t => t.Amount).ToList(), AmountTolerance, out var typical))
                continue;

            var last = items[^1];
            if (RecurringDetector.IsLapsed(last.Date, cadence.Nominal, asOf))
                continue;

            var typicalDay = TypicalDay(items);
            var rule = cadence.Cadence == RecurringCadence.Monthly ? InferWeekendRule(items, typicalDay) : null;
            var next = cadence.Cadence == RecurringCadence.Monthly
                ? NextMonthly(typicalDay, rule ?? fallbackRule, last.Date, asOf)
                : RollForward(last.Date, cadence.Cadence, asOf);

            sources.Add(new IncomeSource(
                SourceName(last),
                decimal.Round(typical, 2),
                cadence.Cadence,
                typicalDay,
                last.Date,
                next,
                items.Count,
                rule,
                items.Select(t => t.Id).ToList()));
        }

        return sources.OrderByDescending(s => RecurringDetector.MonthlyCost(s.Amount, s.Cadence)).ToList();
    }

    // The biggest monthly income is treated as the salary that marks payday.
    public static IncomeSource? Salary(IReadOnlyList<IncomeSource> sources) =>
        sources.Where(s => s.Cadence == RecurringCadence.Monthly).MaxBy(s => s.Amount);

    public static DateOnly AdjustForWeekend(DateOnly date, WeekendPayday rule) => (date.DayOfWeek, rule) switch
    {
        (DayOfWeek.Saturday, WeekendPayday.FridayBefore) => date.AddDays(-1),
        (DayOfWeek.Sunday, WeekendPayday.FridayBefore) => date.AddDays(-2),
        (DayOfWeek.Saturday, WeekendPayday.MondayAfter) => date.AddDays(2),
        (DayOfWeek.Sunday, WeekendPayday.MondayAfter) => date.AddDays(1),
        _ => date,
    };

    // Next pay date from a day-of-month setting, skipping a month that has already been paid.
    public static DateOnly NextPayDate(DateOnly asOf, int payDay, WeekendPayday rule, DateOnly? lastPaid = null)
    {
        var month = YearMonth.From(asOf);
        for (var i = 0; i < 3; i++, month = month.AddMonths(1))
        {
            if (lastPaid is { } paid && month.Contains(paid))
                continue;

            var candidate = AdjustForWeekend(DayIn(month, payDay), rule);
            if (candidate > asOf)
                return candidate;
        }

        return AdjustForWeekend(DayIn(month, payDay), rule);
    }

    // "SALARY ACME HOLDINGS PTY LTD" reads better as "Acme Holdings Pty Ltd".
    private static string SourceName(Transaction t)
    {
        var name = RecurringDetector.DisplayName(t);
        foreach (var prefix in new[] { "Salary ", "Sal ", "Wages ", "Payroll " })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name[prefix.Length..];
        }

        return name;
    }

    private static DateOnly NextMonthly(int typicalDay, WeekendPayday rule, DateOnly lastPaid, DateOnly asOf) =>
        NextPayDate(asOf, typicalDay, rule, lastPaid);

    private static DateOnly RollForward(DateOnly from, RecurringCadence cadence, DateOnly asOf)
    {
        var next = RecurringDetector.NextDate(from, cadence);
        while (next <= asOf)
            next = RecurringDetector.NextDate(next, cadence);
        return next;
    }

    // Most common day of the month it arrives on. Weekend-shifted payments are the minority,
    // so they don't pull the answer away from the real payday.
    private static int TypicalDay(List<Transaction> items) =>
        items.GroupBy(t => t.Date.Day)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First().Key;

    // Looks at months where the usual payday fell on a weekend and checks what the bank did.
    private static WeekendPayday? InferWeekendRule(List<Transaction> items, int typicalDay)
    {
        var votes = new Dictionary<WeekendPayday, int>();
        foreach (var t in items)
        {
            var nominal = DayIn(YearMonth.From(t.Date), typicalDay);
            if (nominal.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                continue;

            var rule = t.Date < nominal ? WeekendPayday.FridayBefore
                : t.Date > nominal ? WeekendPayday.MondayAfter
                : WeekendPayday.SameDay;
            votes[rule] = votes.GetValueOrDefault(rule) + 1;
        }

        return votes.Count == 0 ? null : votes.MaxBy(v => v.Value).Key;
    }

    private static DateOnly DayIn(YearMonth month, int day) => new(month.Year, month.Month, Math.Clamp(day, 1, month.DaysInMonth));
}
