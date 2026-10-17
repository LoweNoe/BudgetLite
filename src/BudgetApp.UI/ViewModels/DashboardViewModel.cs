using System.Globalization;
using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.UI.Controls.Charts;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed record InsightCard(string Text, string Icon, bool IsWarning, bool IsPositive);

public sealed record LegendRow(string CategoryId, string Name, string Colour, string AmountText, string ShareText, string ChangeText, bool IsUp, bool IsDown);

public sealed record RecurringRow(string Merchant, string Icon, string Colour, string Detail, string AmountText, string KindText, bool IsSubscription);

public sealed record UpcomingRow(string Merchant, string DateText, string AmountText, bool AtRisk, bool IsIncome = false);

public sealed record DashboardBudgetRow(string Name, string Colour, string AmountText, double Used, double Projected, bool IsOver, bool IsWatch, string StateText);

public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly BudgetWorkspace _workspace;
    private readonly Navigator _navigator;
    private readonly TipsService _tips;
    private YearMonth? _month;

    public DashboardViewModel(BudgetWorkspace workspace, Navigator navigator, TipsService tips)
    {
        _workspace = workspace;
        _navigator = navigator;
        _tips = tips;

        workspace.TransactionsChanged += (_, _) => Refresh();
        workspace.CategoriesChanged += (_, _) => Refresh();
        workspace.SettingsChanged += (_, _) => Refresh();
        workspace.BudgetsChanged += (_, _) => Refresh();
        tips.TipsReset += (_, _) => ShowTrendTip = !_tips.IsDismissed(TipKeys.DashboardTrend);
        Refresh();
    }

    public string Currency => _workspace.Settings.Currency;

    [ObservableProperty] public partial bool HasData { get; set; }
    [ObservableProperty] public partial string MonthLabel { get; set; } = "";
    [ObservableProperty] public partial string MonthCaption { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousMonthCommand), nameof(NextMonthCommand))]
    public partial IReadOnlyList<YearMonth> Months { get; set; } = [];

    // Headline numbers
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpentText))]
    public partial decimal Spent { get; set; }

    public string SpentText => MoneyFormatter.Format(Spent, Currency, wholeUnits: true);
    [ObservableProperty] public partial decimal Income { get; set; }
    [ObservableProperty] public partial decimal NonEssential { get; set; }
    [ObservableProperty] public partial string NonEssentialShare { get; set; } = "";
    [ObservableProperty] public partial string SpentChangeText { get; set; } = "";
    [ObservableProperty] public partial bool SpentWentUp { get; set; }
    [ObservableProperty] public partial bool SpentWentDown { get; set; }
    [ObservableProperty] public partial bool HasSpentChange { get; set; }
    [ObservableProperty] public partial string IncomeCaption { get; set; } = "";

    // Forecast
    [ObservableProperty] public partial bool HasForecast { get; set; }
    [ObservableProperty] public partial decimal ForecastEnd { get; set; }
    [ObservableProperty] public partial string ForecastCaption { get; set; } = "";
    [ObservableProperty] public partial bool ForecastIsWarning { get; set; }
    [ObservableProperty] public partial string ForecastSummary { get; set; } = "";
    [ObservableProperty] public partial string ForecastUnavailable { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<ChartPoint> ForecastPoints { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<UpcomingRow> UpcomingDebits { get; set; } = [];
    [ObservableProperty] public partial bool HasUpcomingDebits { get; set; }
    [ObservableProperty] public partial string ForecastRange { get; set; } = "";
    [ObservableProperty] public partial string PaydayDetail { get; set; } = "";

    // Budgets
    [ObservableProperty] public partial bool HasBudgets { get; set; }
    [ObservableProperty] public partial IReadOnlyList<DashboardBudgetRow> BudgetRows { get; set; } = [];
    [ObservableProperty] public partial string BudgetsCaption { get; set; } = "";

    // Insights: the dashboard shows only the most important one; the Insights page has the rest.
    [ObservableProperty] public partial InsightCard? LeadInsight { get; set; }
    [ObservableProperty] public partial bool HasInsights { get; set; }
    [ObservableProperty] public partial string InsightsCaption { get; set; } = "";

    // Charts
    [ObservableProperty] public partial IReadOnlyList<ChartSlice> Slices { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<LegendRow> Legend { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ChartBar> TrendBars { get; set; } = [];
    [ObservableProperty] public partial bool HasSpending { get; set; }
    [ObservableProperty] public partial bool ShowTrendTip { get; set; }

    // Recurring and fees
    [ObservableProperty] public partial IReadOnlyList<RecurringRow> Recurring { get; set; } = [];
    [ObservableProperty] public partial bool HasRecurring { get; set; }
    [ObservableProperty] public partial decimal RecurringMonthly { get; set; }
    [ObservableProperty] public partial decimal FeesThisMonth { get; set; }
    [ObservableProperty] public partial decimal FeesThisYear { get; set; }
    [ObservableProperty] public partial string FeesSentence { get; set; } = "";

    [RelayCommand]
    private void GoToImport() => _navigator.GoTo(Page.Import);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) - 1]);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextMonth() => ShowMonth(Months[Months.ToList().IndexOf(_month!.Value) + 1]);

    [RelayCommand]
    private void SelectMonth(object? month)
    {
        if (month is YearMonth m)
            ShowMonth(m);
    }

    [RelayCommand]
    private void OpenInsights() => _navigator.GoTo(Page.Insights);

    [RelayCommand]
    private void OpenCategory(LegendRow? row)
    {
        if (row is not null && _month is { } month)
            _navigator.ShowTransactions(new TransactionFilter(row.CategoryId, month));
    }

    [RelayCommand]
    private void OpenMonth() => _navigator.ShowTransactions(new TransactionFilter(Month: _month));

    [RelayCommand]
    private void OpenBudgets() => _navigator.GoTo(Page.Budgets);

    [RelayCommand]
    private async Task DismissTrendTip()
    {
        ShowTrendTip = false;
        await _tips.DismissAsync(TipKeys.DashboardTrend);
    }

    private bool CanGoPrevious() => _month is { } m && Months.Count > 0 && m > Months[0];

    private bool CanGoNext() => _month is { } m && Months.Count > 0 && m < Months[^1];

    private void ShowMonth(YearMonth month)
    {
        _month = month;
        Refresh();
    }

    private void Refresh()
    {
        var report = BudgetAnalyzer.Analyse(_workspace.Transactions, _workspace.Categories, _workspace.Settings, _month, _workspace.Budgets);
        HasData = report is not null;
        OnPropertyChanged(nameof(Currency));
        if (report is null)
        {
            _month = null;
            return;
        }

        _month = report.Month;
        Months = report.AvailableMonths;
        PreviousMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();

        MonthLabel = report.Month.ToString();
        MonthCaption = report.ComparedThroughDay is { } day
            ? $"1–{day} {report.Month.First:MMMM} so far, compared with the same days last month"
            : $"{Display.Count(report.Summary.TransactionCount, "transaction")} this month";
        ShowTrendTip = report.AvailableMonths.Count > 1 && !_tips.IsDismissed(TipKeys.DashboardTrend);

        ApplyHeadlines(report);
        ApplyInsights(report);
        ApplyCharts(report);
        ApplyForecast(report);
        ApplyRecurring(report);
        ApplyBudgets(report);
    }

    private void ApplyBudgets(AnalysisReport report)
    {
        HasBudgets = report.Budgets is { Items.Count: > 0 };
        if (report.Budgets is not { } budgets)
        {
            BudgetRows = [];
            BudgetsCaption = "";
            return;
        }

        BudgetRows = budgets.Items.Take(4)
            .Select(b => new DashboardBudgetRow(
                b.Name,
                b.Colour,
                $"{MoneyFormatter.Format(b.Spent, Currency, wholeUnits: true)} of {MoneyFormatter.Format(b.Limit, Currency, wholeUnits: true)}",
                b.UsedFraction,
                b.ProjectedFraction,
                b.State == BudgetState.Over,
                b.State == BudgetState.Watch,
                b.State switch { BudgetState.Over => "Over", BudgetState.Watch => "Watch", _ => "On track" }))
            .ToList();

        var left = budgets.TotalLimit - budgets.TotalSpent;
        BudgetsCaption = left >= 0
            ? $"{MoneyFormatter.Format(left, Currency, wholeUnits: true)} left across {Display.Count(budgets.Items.Count, "budget")}"
            : $"{MoneyFormatter.Format(-left, Currency, wholeUnits: true)} over across {Display.Count(budgets.Items.Count, "budget")}";
    }

    private void ApplyHeadlines(AnalysisReport report)
    {
        var summary = report.Summary;
        Spent = summary.Spent;
        Income = summary.Income;
        NonEssential = summary.NonEssential;
        NonEssentialShare = summary.Spent > 0 ? $"{Math.Round(summary.NonEssential / summary.Spent * 100):0}% of spending" : "Nothing yet";
        IncomeCaption = summary.Income > summary.Spent
            ? $"{MoneyFormatter.Format(summary.Income - summary.Spent, Currency, wholeUnits: true)} more in than out"
            : summary.Income > 0 ? "Less came in than went out" : "No income recorded";

        if (report.Previous is { Spent: > 0 } previous)
        {
            var change = (summary.Spent - previous.Spent) / previous.Spent;
            HasSpentChange = true;
            SpentWentUp = change > 0.005m;
            SpentWentDown = change < -0.005m;
            var versus = report.ComparedThroughDay is null ? "last month" : "this time last month";
            SpentChangeText = SpentWentUp || SpentWentDown ? $"{Math.Abs(Math.Round(change * 100)):0}% vs {versus}" : $"Same as {versus}";
        }
        else
        {
            HasSpentChange = false;
            SpentWentUp = SpentWentDown = false;
            SpentChangeText = "";
        }

        FeesThisMonth = summary.BankFees;
        FeesThisYear = report.BankFeesYearToDate;
        FeesSentence = summary.BankFees > 0
            ? $"That's {MoneyFormatter.Format(report.BankFeesYearToDate * 12 / Math.Max(1, report.Month.Month), Currency, wholeUnits: true)} a year at this rate."
            : "No bank fees this month.";
    }

    private void ApplyInsights(AnalysisReport report)
    {
        var lead = report.Insights.FirstOrDefault();
        HasInsights = lead is not null;
        LeadInsight = lead is null ? null : new InsightCard(lead.Text, lead.Icon, lead.Kind == InsightKind.Warning, lead.Kind == InsightKind.Positive);

        var warnings = report.Insights.Count(i => i.Kind == InsightKind.Warning);
        var others = report.Insights.Count - 1;
        InsightsCaption = others <= 0
            ? "That's the only thing that stands out this month."
            : warnings > 1
                ? $"{warnings} things need your attention, and {Display.Count(report.Insights.Count - warnings, "more insight")} on the Insights page."
                : $"{Display.Count(others, "more insight")} on the Insights page.";
    }

    private void ApplyCharts(AnalysisReport report)
    {
        var spending = report.Categories.Where(c => c.Amount > 0).ToList();
        var total = spending.Sum(c => c.Amount);
        HasSpending = total > 0;

        Slices = spending
            .Select(c => new ChartSlice(c.Name, (double)c.Amount, c.Colour, MoneyFormatter.Format(c.Amount, Currency), c.CategoryId))
            .ToList();

        Legend = spending
            .Select(c => new LegendRow(
                c.CategoryId,
                c.Name,
                c.Colour,
                MoneyFormatter.Format(c.Amount, Currency),
                total > 0 ? $"{Math.Round(c.Amount / total * 100):0}%" : "",
                ChangeText(c),
                c.PreviousAmount > 0 && c.Change > 0,
                c.PreviousAmount > 0 && c.Change < 0))
            .ToList();

        TrendBars = report.Trend
            .Select(m => new ChartBar(
                m.Month.ShortName,
                (double)m.Spent,
                (double)m.NonEssential,
                $"{MoneyFormatter.Format(m.Spent, Currency, wholeUnits: true)} spent · {MoneyFormatter.Format(m.NonEssential, Currency, wholeUnits: true)} non-essential",
                m.Month == report.Month,
                report.AvailableMonths.Contains(m.Month) ? m.Month : null))
            .ToList();
    }

    private string ChangeText(CategoryTotal c)
    {
        if (c.PreviousAmount <= 0 || c.ChangePercent is not { } percent)
            return "New";
        if (Math.Abs(percent) < 0.01)
            return "Same";
        return $"{Math.Abs(Math.Round(percent * 100)):0}%";
    }

    private void ApplyForecast(AnalysisReport report)
    {
        var forecast = report.Forecast;
        HasForecast = forecast is not null && report.IsLatestMonth;

        if (forecast is null)
        {
            ForecastUnavailable = "Your statement doesn't include a balance column, so we can't project your balance yet.";
            ForecastPoints = [];
            UpcomingDebits = [];
            HasUpcomingDebits = false;
            return;
        }

        if (!report.IsLatestMonth)
            ForecastUnavailable = "The payday forecast looks ahead from your latest month.";

        string Whole(decimal amount) => MoneyFormatter.Format(amount, Currency, wholeUnits: true);

        ForecastEnd = forecast.EndBalance;
        ForecastIsWarning = forecast.RunsOutOn is not null;
        ForecastCaption = forecast.RunsOutOn is { } runsOut
            ? $"Could run out on {Short(runsOut)}"
            : $"Left on payday, {Weekday(forecast.PayDate)}";
        ForecastRange = forecast.DaysToPayday > 0 ? $"Likely {Whole(forecast.EndLow)} to {Whole(forecast.EndHigh)}" : "";

        // Explain where the pay date comes from, including any weekend shift.
        var nominal = forecast.Salary is { } salary
            ? new DateOnly(forecast.PayDate.Year, forecast.PayDate.Month, Math.Clamp(salary.TypicalDay, 1, DateTime.DaysInMonth(forecast.PayDate.Year, forecast.PayDate.Month)))
            : new DateOnly(forecast.PayDate.Year, forecast.PayDate.Month, Math.Clamp(_workspace.Settings.PayDay, 1, DateTime.DaysInMonth(forecast.PayDate.Year, forecast.PayDate.Month)));
        var shifted = nominal != forecast.PayDate ? $" (the {Ordinal(nominal.Day)} is a {nominal.DayOfWeek})" : "";
        PaydayDetail = forecast.Salary is { } s
            ? $"Payday {Weekday(forecast.PayDate)}{shifted}, from your {Whole(s.Amount)} salary from {s.Name}."
            : $"Payday {Weekday(forecast.PayDate)}{shifted}, from your settings. Import a few months with your salary and we'll detect it.";

        var spend = forecast.Spend;
        var pattern = spend.WeekendAverage > spend.WeekdayAverage * 1.2m
            ? $"about {Whole(spend.WeekdayAverage)} on weekdays and {Whole(spend.WeekendAverage)} on weekend days"
            : $"about {Whole(spend.AverageDaily)} a day";
        var extras = new List<string>();
        if (forecast.UpcomingDebits.Count > 0)
            extras.Add(Display.Count(forecast.UpcomingDebits.Count, "scheduled payment"));
        if (forecast.UpcomingIncome.Count > 0)
            extras.Add($"{Whole(forecast.UpcomingIncome.Sum(i => i.Source.Amount))} of expected income");
        var oneOffs = spend.OneOffCount > 0
            ? $" We've left out {Display.Count(spend.OneOffCount, "one-off purchase")} ({Whole(spend.OneOffTotal)}) since they're unusual."
            : "";
        ForecastSummary = $"Based on your usual spending, {pattern}{(extras.Count > 0 ? ", plus " + string.Join(" and ", extras) : "")}.{oneOffs}";

        var dueByDate = forecast.UpcomingDebits.ToLookup(d => d.Date);
        var incomeByDate = forecast.UpcomingIncome.ToLookup(i => i.Date);
        ForecastPoints = forecast.Points
            .Select(p =>
            {
                var due = dueByDate[p.Date].ToList();
                var income = incomeByDate[p.Date].ToList();
                var labels = due.Select(d => d.Charge.Merchant).Concat(income.Select(i => $"+ {i.Source.Name}")).ToList();
                var marker = labels.Count == 0 ? null : string.Join(", ", labels);
                var range = p.High > p.Low ? $" (likely {Whole(p.Low)} to {Whole(p.High)})" : "";
                return new ChartPoint(Short(p.Date), (double)p.Balance, MoneyFormatter.Format(p.Balance, Currency) + range, marker,
                    due.Any(d => d.AtRisk), (double)p.Low, (double)p.High);
            })
            .ToList();

        UpcomingDebits = forecast.UpcomingIncome
            .Select(i => (i.Date, Row: new UpcomingRow(i.Source.Name, Short(i.Date), "+" + MoneyFormatter.Format(i.Source.Amount, Currency), false, IsIncome: true)))
            .Concat(forecast.UpcomingDebits.Select(d => (d.Date, Row: new UpcomingRow(d.Charge.Merchant, Short(d.Date), MoneyFormatter.Format(d.Charge.Amount, Currency), d.AtRisk))))
            .OrderBy(x => x.Date)
            .Select(x => x.Row)
            .ToList();
        HasUpcomingDebits = UpcomingDebits.Count > 0;
    }

    private void ApplyRecurring(AnalysisReport report)
    {
        Recurring = report.Recurring
            .Select(r =>
            {
                var category = _workspace.FindCategory(r.CategoryId);
                var cadence = r.Cadence.ToString().ToLowerInvariant();
                return new RecurringRow(
                    r.Merchant,
                    category?.Icon ?? "repeat",
                    category?.Colour ?? "chart-8",
                    $"{char.ToUpperInvariant(cadence[0])}{cadence[1..]} · next on {Short(r.NextDate)}",
                    MoneyFormatter.Format(r.Amount, Currency),
                    r.Kind switch
                    {
                        RecurringKind.Subscription => "Subscription",
                        RecurringKind.DebitOrder => "Debit order",
                        _ => "Regular",
                    },
                    r.Kind == RecurringKind.Subscription);
            })
            .ToList();
        HasRecurring = Recurring.Count > 0;
        RecurringMonthly = report.RecurringMonthlyTotal;
    }

    private static string Short(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);

    private static string Weekday(DateOnly date) => date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private static string Ordinal(int day) => day + (day % 100 is 11 or 12 or 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
