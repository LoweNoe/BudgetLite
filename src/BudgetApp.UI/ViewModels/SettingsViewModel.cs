using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using BudgetApp.Core.Analysis;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.Data;
using BudgetApp.Import;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly BudgetWorkspace _workspace;
    private readonly IPlatformDialogs _platform;
    private readonly Navigator _navigator;
    private readonly ToastService _toasts;
    private readonly DialogService _dialogs;
    private readonly TipsService _tips;
    private readonly BackupService _backups;
    private readonly ThemeService _theme;
    private readonly DataPaths _paths;
    private readonly AppLaunchOptions _launch;
    private bool _loading;

    public SettingsViewModel(
        BudgetWorkspace workspace,
        ThemeService theme,
        IPlatformDialogs platform,
        Navigator navigator,
        ToastService toasts,
        DialogService dialogs,
        TipsService tips,
        BackupService backups,
        DataPaths paths,
        AppLaunchOptions launch)
    {
        _workspace = workspace;
        _platform = platform;
        _navigator = navigator;
        _toasts = toasts;
        _dialogs = dialogs;
        _tips = tips;
        _backups = backups;
        _theme = theme;
        _paths = paths;
        _launch = launch;
        Theme = new ThemeChoiceViewModel(theme);

        workspace.SettingsChanged += (_, _) => LoadFromSettings();
        workspace.TransactionsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DataSummary));
            UpdateSalaryHint();
        };
        LoadFromSettings();
    }

    public ThemeChoiceViewModel Theme { get; }
    public IReadOnlyList<string> Currencies => MoneyFormatter.SupportedCurrencies;
    public string DataFolder => _workspace.DataLocation;
    public bool CanMoveFolder => !_launch.CustomDataDir;

    public string DataSummary =>
        $"{Display.Count(_workspace.Transactions.Count, "transaction")}, {Display.Count(_workspace.Categories.Count, "category", "categories")} and {Display.Count(_workspace.Rules.Count, "rule")}.";

    [ObservableProperty] public partial decimal? PayDay { get; set; }
    [ObservableProperty] public partial string SelectedCurrency { get; set; } = "ZAR";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial WeekendOption? SelectedWeekendOption { get; set; }
    [ObservableProperty] public partial string SalaryHint { get; set; } = "";
    [ObservableProperty] public partial bool HasSalaryHint { get; set; }
    [ObservableProperty] public partial bool CanUseDetectedDay { get; set; }
    [ObservableProperty] public partial string UseDetectedDayText { get; set; } = "";

    private int? _detectedDay;

    public IReadOnlyList<WeekendOption> WeekendOptions => WeekendOption.All;

    partial void OnSelectedWeekendOptionChanged(WeekendOption? value)
    {
        if (_loading || value is null)
            return;

        _workspace.Settings.WeekendPayday = value.Rule;
        _ = _workspace.SaveSettingsAsync();
    }

    [RelayCommand]
    private void UseDetectedDay()
    {
        if (_detectedDay is { } day)
            PayDay = day;
    }

    partial void OnPayDayChanged(decimal? value)
    {
        if (_loading || value is null)
            return;

        _workspace.Settings.PayDay = (int)Math.Clamp(value.Value, 1, 31);
        _ = _workspace.SaveSettingsAsync();
    }

    partial void OnSelectedCurrencyChanged(string value)
    {
        if (_loading)
            return;

        _workspace.Settings.Currency = value;
        _ = _workspace.SaveSettingsAsync();
    }

    [RelayCommand]
    private Task OpenDataFolder() => _platform.OpenFolderAsync(DataFolder);

    [RelayCommand]
    private void RerunOnboarding() => _navigator.ShowOnboarding();

    [RelayCommand]
    private async Task ResetTips()
    {
        await _tips.ResetAsync();
        _toasts.Show("Tips will show again where they're useful.");
    }

    [RelayCommand]
    private async Task CreateBackup()
    {
        var stream = await _platform.SaveFileAsync("Save a backup", $"BudgetLite backup {DateTime.Now:yyyy-MM-dd}.zip", "Zip archive", "zip");
        if (stream is null)
            return;

        await RunBusyAsync(async () =>
        {
            await using (stream)
                await _backups.CreateAsync(stream);
            _toasts.Show("Backup saved. Keep it somewhere safe, like a USB stick.");
        });
    }

    [RelayCommand]
    private async Task RestoreBackup()
    {
        var stream = await _platform.OpenFileAsync("Choose a backup to restore", "BudgetLite backup", "zip");
        if (stream is null)
            return;

        await using (stream)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Restore this backup?",
                "Your current transactions, categories, rules and settings will be replaced. We'll save a safety copy of them first.",
                "Restore", destructive: true);
            if (!confirmed)
                return;

            await RunBusyAsync(async () =>
            {
                var result = await _backups.RestoreAsync(stream);
                if (!result.Success)
                {
                    _toasts.Show(result.Error ?? "That backup couldn't be restored.", ToastKind.Warning);
                    return;
                }

                await _workspace.ReloadAsync();
                _theme.ApplyCurrent();
                _toasts.Show($"Backup restored. {DataSummary}");
            });
        }
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        if (_workspace.Transactions.Count == 0)
        {
            _toasts.Show("There are no transactions to export yet.", ToastKind.Info);
            return;
        }

        var stream = await _platform.SaveFileAsync("Export transactions", $"BudgetLite transactions {DateTime.Now:yyyy-MM-dd}.csv", "CSV file", "csv");
        if (stream is null)
            return;

        await RunBusyAsync(async () =>
        {
            await using (stream)
                await TransactionCsvExporter.WriteAsync(stream, _workspace.Transactions, _workspace.Categories);
            _toasts.Show($"Exported {Display.Count(_workspace.Transactions.Count, "transaction")}.");
        });
    }

    [RelayCommand]
    private async Task ChangeDataFolder()
    {
        if (!CanMoveFolder)
            return;

        var folder = await _platform.PickFolderAsync("Choose where to keep your data");
        if (folder is null || string.Equals(Path.GetFullPath(folder), Path.GetFullPath(DataFolder), StringComparison.OrdinalIgnoreCase))
            return;

        var hasData = DataLocation.HasData(folder);
        var confirmed = hasData
            ? await _dialogs.ConfirmAsync("Switch to the data in this folder?",
                $"{folder} already has BudgetLite data. We'll use that from now on and leave your current data where it is. BudgetLite will restart.",
                "Switch and restart")
            : await _dialogs.ConfirmAsync("Move your data here?",
                $"We'll copy your data to {folder} and use it from now on. Your current files stay as a backup. BudgetLite will restart.",
                "Move and restart");
        if (!confirmed)
            return;

        await RunBusyAsync(async () =>
        {
            await DataLocation.MoveAsync(_paths, folder, copyFiles: !hasData);
            Restart();
        });
    }

    [RelayCommand]
    private async Task DeleteAllTransactions()
    {
        if (_workspace.Transactions.Count == 0)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Delete all transactions?",
            $"This removes {Display.Count(_workspace.Transactions.Count, "transaction")}. Your categories, rules and settings stay. Consider making a backup first.",
            "Delete transactions", destructive: true);
        if (!confirmed)
            return;

        await _workspace.ClearTransactionsAsync();
        _toasts.Show("All transactions deleted.");
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        catch (IOException ex)
        {
            _toasts.Show($"That didn't work: {ex.Message}", ToastKind.Warning);
        }
        catch (UnauthorizedAccessException)
        {
            _toasts.Show("BudgetLite isn't allowed to write there. Try another folder.", ToastKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadFromSettings()
    {
        _loading = true;
        PayDay = _workspace.Settings.PayDay;
        SelectedCurrency = _workspace.Settings.Currency;
        SelectedWeekendOption = WeekendOption.All.First(o => o.Rule == _workspace.Settings.WeekendPayday);
        _loading = false;
        OnPropertyChanged(nameof(DataSummary));
        UpdateSalaryHint();
    }

    private void UpdateSalaryHint()
    {
        var transactions = _workspace.Transactions;
        var salary = transactions.Count == 0
            ? null
            : IncomeDetector.Salary(IncomeDetector.Detect(transactions, transactions.Max(t => t.Date), _workspace.Settings.WeekendPayday));

        _detectedDay = salary?.TypicalDay;
        HasSalaryHint = salary is not null;
        if (salary is null)
        {
            SalaryHint = "";
            CanUseDetectedDay = false;
            return;
        }

        var weekend = salary.WeekendRule switch
        {
            WeekendPayday.FridayBefore => " When that's a weekend, it arrives the Friday before.",
            WeekendPayday.MondayAfter => " When that's a weekend, it arrives the Monday after.",
            _ => "",
        };
        SalaryHint = $"We spotted your salary: {salary.Name}, about {MoneyFormatter.Format(salary.Amount, _workspace.Settings.Currency, wholeUnits: true)} around the {Ordinal(salary.TypicalDay)} of each month.{weekend} The forecast uses this automatically.";
        CanUseDetectedDay = salary.TypicalDay != _workspace.Settings.PayDay;
        UseDetectedDayText = $"Use the {Ordinal(salary.TypicalDay)}";
    }

    private static string Ordinal(int day) => day + (day % 100 is 11 or 12 or 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static void Restart()
    {
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
