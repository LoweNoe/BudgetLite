using BudgetApp.Core.Models;

namespace BudgetApp.Core.Analysis;

public static class ForecastCalculator
{
    private const int SpendWindowDays = 84;
    private const int MinimumDaysForWeekdayPattern = 14;

    // 80% of outcomes fall within ±1.28 standard deviations.
    private const decimal BandZ = 1.28m;

    public static DateOnly NextPayday(DateOnly asOf, int payDay)
    {
        var thisMonth = PaydayIn(asOf.Year, asOf.Month, payDay);
        if (thisMonth > asOf)
            return thisMonth;

        var next = asOf.AddMonths(1);
        return PaydayIn(next.Year, next.Month, payDay);
    }

    // Walks day by day from the last known balance to payday. Each day takes off the typical
    // spend for that weekday; scheduled debits and other regular income land on their dates.
    // The likely range widens with the square root of days ahead, because daily wobbles partly cancel out.
    public static PaydayForecast? Calculate(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<RecurringCharge> recurring,
        IReadOnlyList<IncomeSource> income,
        AppSettings settings)
    {
        if (transactions.Count == 0)
            return null;

        var asOf = transactions.Max(t => t.Date);
        var anchorIndex = -1;
        for (var i = 0; i < transactions.Count; i++)
        {
            var t = transactions[i];
            if (t.Balance is null)
                continue;
            if (anchorIndex < 0 || t.Date >= transactions[anchorIndex].Date)
                anchorIndex = i;
        }

        if (anchorIndex < 0)
            return null;

        var anchor = transactions[anchorIndex];
        var start = anchor.Balance!.Value + transactions.Where(t => t.Date > anchor.Date).Sum(t => t.Amount);

        var salary = IncomeDetector.Salary(income);
        var (payDate, source) = salary is not null
            ? (salary.NextDate, PayDateSource.DetectedSalary)
            : (IncomeDetector.NextPayDate(asOf, settings.PayDay, settings.WeekendPayday), PayDateSource.Settings);

        var recurringIds = recurring.SelectMany(r => r.TransactionIds).ToHashSet();
        var spend = BuildSpendModel(transactions, recurringIds, asOf);

        var dues = recurring
            .SelectMany(r => Occurrences(r.NextDate, r.Cadence, asOf, payDate).Select(d => (Charge: r, Date: d)))
            .ToLookup(x => x.Date);
        var credits = income
            .Where(s => s != salary)
            .SelectMany(s => Occurrences(s.NextDate, s.Cadence, asOf, payDate.AddDays(-1)).Select(d => new UpcomingIncome(s, d)))
            .ToList();
        var creditsByDay = credits.ToLookup(c => c.Date);

        var points = new List<ForecastPoint> { new(asOf, start, start, start) };
        var upcoming = new List<UpcomingDebit>();
        var balance = start;
        DateOnly? runsOut = start < 0 ? asOf : null;
        var days = 0;

        for (var day = asOf.AddDays(1); day <= payDate; day = day.AddDays(1))
        {
            days++;
            balance -= spend.For(day.DayOfWeek);

            foreach (var credit in creditsByDay[day])
                balance += credit.Source.Amount;

            foreach (var due in dues[day])
            {
                upcoming.Add(new UpcomingDebit(due.Charge, day, decimal.Round(balance, 2)));
                balance -= due.Charge.Amount;
            }

            balance = decimal.Round(balance, 2);
            var spread = decimal.Round(BandZ * spend.DailyStdDev * (decimal)Math.Sqrt(days), 2);
            points.Add(new ForecastPoint(day, balance, balance - spread, balance + spread));

            if (runsOut is null && balance < 0)
                runsOut = day;
        }

        var end = points[^1];
        return new PaydayForecast(
            asOf,
            payDate,
            source,
            salary,
            start,
            end.Balance,
            end.Low,
            end.High,
            points.Min(p => p.Balance),
            spend,
            points,
            upcoming,
            credits,
            runsOut);
    }

    // Typical everyday spending per weekday over the last 12 weeks. Recurring charges and transfers
    // are handled separately, and unusually large one-off purchases (beyond Q3 + 3×IQR) are set
    // aside so a new fridge doesn't make every future day look expensive.
    public static SpendModel BuildSpendModel(IReadOnlyList<Transaction> transactions, ISet<Guid> recurringIds, DateOnly asOf)
    {
        var earliest = transactions.Min(t => t.Date);
        var windowStart = asOf.AddDays(-(SpendWindowDays - 1));
        if (windowStart < earliest)
            windowStart = earliest;
        var windowDays = Math.Max(1, asOf.DayNumber - windowStart.DayNumber + 1);

        var everyday = transactions
            .Where(t => t.Date >= windowStart && t.Date <= asOf && t.Amount < 0 && !recurringIds.Contains(t.Id) && t.CategoryId != CategoryIds.Transfers)
            .ToList();

        var threshold = OneOffThreshold(everyday.Select(t => -t.Amount).ToList());
        var oneOffs = everyday.Where(t => -t.Amount > threshold).ToList();
        var kept = everyday.Where(t => -t.Amount <= threshold).ToList();

        var dailyTotals = new decimal[windowDays];
        foreach (var t in kept)
            dailyTotals[t.Date.DayNumber - windowStart.DayNumber] += -t.Amount;

        var average = dailyTotals.Sum() / windowDays;
        var variance = dailyTotals.Sum(d => (d - average) * (d - average)) / windowDays;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var byWeekday = new decimal[7];
        if (windowDays >= MinimumDaysForWeekdayPattern)
        {
            var counts = new int[7];
            for (var i = 0; i < windowDays; i++)
            {
                var weekday = (int)windowStart.AddDays(i).DayOfWeek;
                counts[weekday]++;
                byWeekday[weekday] += dailyTotals[i];
            }

            for (var d = 0; d < 7; d++)
                byWeekday[d] = counts[d] == 0 ? average : decimal.Round(byWeekday[d] / counts[d], 2);
        }
        else
        {
            Array.Fill(byWeekday, decimal.Round(average, 2));
        }

        return new SpendModel(byWeekday, decimal.Round(average, 2), decimal.Round(stdDev, 2), windowDays, oneOffs.Count, -oneOffs.Sum(t => t.Amount));
    }

    private static decimal OneOffThreshold(List<decimal> amounts)
    {
        if (amounts.Count < 8)
            return decimal.MaxValue;

        var sorted = amounts.Order().ToList();
        var q1 = sorted[sorted.Count / 4];
        var q3 = sorted[sorted.Count * 3 / 4];
        return Math.Max(q3 + 3 * (q3 - q1), q3 * 2);
    }

    private static IEnumerable<DateOnly> Occurrences(DateOnly first, RecurringCadence cadence, DateOnly after, DateOnly until)
    {
        for (var date = first; date <= until; date = RecurringDetector.NextDate(date, cadence))
        {
            if (date > after)
                yield return date;
        }
    }

    private static DateOnly PaydayIn(int year, int month, int payDay) =>
        new(year, month, Math.Clamp(payDay, 1, DateTime.DaysInMonth(year, month)));
}
