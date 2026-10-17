using System.Collections.ObjectModel;
using BudgetApp.Core.Abstractions;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Services;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public sealed partial class StepDot : ObservableObject
{
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool IsCurrent { get; set; }
}

public sealed record NumberedStep(string Number, string Text);

public sealed record BankTipViewModel(string Bank, string Summary, IReadOnlyList<NumberedStep> Steps);

public sealed partial class OnboardingViewModel : ViewModelBase
{
    private const int StepCount = 6;
    private const int ImportStepIndex = 4;
    private const int DoneStepIndex = 5;

    private readonly BudgetWorkspace _workspace;
    private readonly IBankTipsProvider _tipsProvider;

    public OnboardingViewModel(BudgetWorkspace workspace, ThemeService theme, IBankTipsProvider tipsProvider, ImportViewModel import)
    {
        _workspace = workspace;
        _tipsProvider = tipsProvider;
        Theme = new ThemeChoiceViewModel(theme);
        Import = import;
        Import.IsEmbedded = true;
        Import.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImportViewModel.IsMidFlow))
                OnPropertyChanged(nameof(ShowFooter));
        };
        Import.Completed += (_, _) =>
        {
            OnPropertyChanged(nameof(NextText));
            if (StepIndex == ImportStepIndex)
                StepIndex = DoneStepIndex;
        };

        PayDay = workspace.Settings.PayDay;
        SelectedCurrency = workspace.Settings.Currency;
        SelectedWeekendOption = WeekendOption.All.First(o => o.Rule == workspace.Settings.WeekendPayday);
        Dots = Enumerable.Range(0, StepCount).Select(_ => new StepDot()).ToList();
        UpdateDots();
        _ = LoadTipsAsync();
    }

    public event EventHandler<Page>? Finished;

    public ThemeChoiceViewModel Theme { get; }
    public ImportViewModel Import { get; }
    public IReadOnlyList<StepDot> Dots { get; }
    public IReadOnlyList<string> Currencies => MoneyFormatter.SupportedCurrencies;
    public ObservableCollection<BankTipViewModel> BankTips { get; } = [];

    // Hidden while an import is under way so there's only ever one set of buttons on screen.
    public bool ShowFooter => !(IsImportStep && Import.IsMidFlow);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepLabel), nameof(NextText), nameof(CanGoBack), nameof(ShowSkip), nameof(DoneMessage), nameof(ShowFooter),
        nameof(IsWelcome), nameof(IsThemeStep), nameof(IsPaydayStep), nameof(IsBankStep), nameof(IsImportStep), nameof(IsDoneStep))]
    public partial int StepIndex { get; set; }

    [ObservableProperty] public partial decimal? PayDay { get; set; }
    [ObservableProperty] public partial string SelectedCurrency { get; set; } = "ZAR";
    [ObservableProperty] public partial WeekendOption? SelectedWeekendOption { get; set; }

    public IReadOnlyList<WeekendOption> WeekendOptions => WeekendOption.All;
    [ObservableProperty] public partial BankTipViewModel? SelectedBank { get; set; }

    public string StepLabel => $"Step {StepIndex + 1} of {StepCount}";
    public bool CanGoBack => StepIndex > 0;
    public bool ShowSkip => StepIndex < DoneStepIndex;
    public bool IsWelcome => StepIndex == 0;
    public bool IsThemeStep => StepIndex == 1;
    public bool IsPaydayStep => StepIndex == 2;
    public bool IsBankStep => StepIndex == 3;
    public bool IsImportStep => StepIndex == ImportStepIndex;
    public bool IsDoneStep => StepIndex == DoneStepIndex;

    public string NextText => StepIndex switch
    {
        0 => "Get started",
        ImportStepIndex when Import.ImportedCount == 0 => "I'll do this later",
        DoneStepIndex => "Go to dashboard",
        _ => "Next",
    };

    public string DoneMessage => Import.ImportedCount > 0
        ? $"{Display.Count(Import.ImportedCount, "transaction")} imported. Your dashboard is ready."
        : "You can import a statement any time from the Import page.";

    partial void OnStepIndexChanged(int value) => UpdateDots();

    // Opening the app with statement files on first run jumps straight to the import step.
    public async Task ImportFilesAsync(IReadOnlyList<BudgetApp.Core.Import.StatementFile> files)
    {
        StepIndex = ImportStepIndex;
        await Import.LoadFilesAsync(files);
    }

    [RelayCommand]
    private async Task Next()
    {
        if (StepIndex >= DoneStepIndex)
            await FinishAsync();
        else
            StepIndex++;
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > 0)
            StepIndex--;
    }

    [RelayCommand]
    private Task Skip() => FinishAsync();

    private async Task FinishAsync()
    {
        var settings = _workspace.Settings;
        settings.PayDay = (int)Math.Clamp(PayDay ?? settings.PayDay, 1, 31);
        settings.Currency = SelectedCurrency;
        settings.WeekendPayday = SelectedWeekendOption?.Rule ?? settings.WeekendPayday;
        settings.OnboardingCompleted = true;
        await _workspace.SaveSettingsAsync();

        Finished?.Invoke(this, Import.ImportedCount > 0 ? Page.Dashboard : Page.Import);
    }

    private async Task LoadTipsAsync()
    {
        foreach (var tip in await _tipsProvider.GetTipsAsync())
        {
            var steps = tip.Steps.Select((s, i) => new NumberedStep($"{i + 1}", s)).ToList();
            BankTips.Add(new BankTipViewModel(tip.Bank, tip.Summary, steps));
        }

        SelectedBank = BankTips.FirstOrDefault();
    }

    private void UpdateDots()
    {
        for (var i = 0; i < Dots.Count; i++)
        {
            Dots[i].IsActive = i <= StepIndex;
            Dots[i].IsCurrent = i == StepIndex;
        }
    }
}
