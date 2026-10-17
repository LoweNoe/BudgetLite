using System.Collections.ObjectModel;
using BudgetApp.Core.Formatting;
using BudgetApp.Core.Import;
using BudgetApp.Core.Models;
using BudgetApp.Core.Services;
using BudgetApp.Import;
using BudgetApp.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BudgetApp.UI.ViewModels;

public enum ImportStep { ChooseFile, Unlock, MapColumns, Preview, Review, Done }

public sealed partial class StepItem : ObservableObject
{
    public StepItem(ImportStep step, int number, string title)
    {
        Step = step;
        Number = number;
        Title = title;
    }

    public ImportStep Step { get; }
    public int Number { get; }
    public string Title { get; }
    public bool IsFirst => Number == 1;
    public bool HasConnector => !IsFirst;

    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsDone { get; set; }
}

public sealed record ColumnOption(int? Index, string Name);

public sealed record PreviewRow(string DateText, string Description, string AmountText, bool IsCredit, bool IsDuplicate);

public sealed record CategoryCount(string Name, string Colour, string CountText);

public sealed record FileSummary(string FileName, string Icon, string Detail, string? BankName, string StatusText, bool IsGood, bool IsWarning, bool IsFailed)
{
    public bool HasBank => BankName is not null;
}

public sealed partial class ImportViewModel : ViewModelBase
{
    private const int PreviewLimit = 200;

    private readonly StatementImporter _importer;
    private readonly BudgetWorkspace _workspace;
    private readonly IPlatformDialogs _dialogs;
    private readonly Navigator _navigator;
    private readonly ToastService _toasts;

    // Files are worked through one at a time; the flow pauses on a file that needs a password or column mapping.
    private readonly Queue<StatementFile> _queue = new();
    private readonly List<(StatementFile File, ParseResult Result)> _parsed = [];
    private readonly List<FileSummary> _failures = [];
    private StatementFile? _current;
    private ColumnMapping? _detectedMapping;
    private string? _currentSignature;
    private ImportPreview? _preview;
    private List<Transaction> _imported = [];
    private bool _needsUnlockStep;
    private bool _needsMappingStep;

    public ImportViewModel(StatementImporter importer, BudgetWorkspace workspace, IPlatformDialogs dialogs, Navigator navigator, ToastService toasts)
    {
        _importer = importer;
        _workspace = workspace;
        _dialogs = dialogs;
        _navigator = navigator;
        _toasts = toasts;
        BuildSteps();
    }

    public event EventHandler? Completed;

    // True when hosted inside the onboarding wizard, which provides its own navigation.
    public bool IsEmbedded { get; set; }
    public bool ShowViewTransactions => !IsEmbedded;

    public ObservableCollection<StepItem> Steps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChooseFile), nameof(IsUnlock), nameof(IsMapColumns), nameof(IsPreview), nameof(IsReview), nameof(IsDone), nameof(StepSentence), nameof(IsFinished), nameof(IsMidFlow))]
    public partial ImportStep CurrentStep { get; set; }

    // Somewhere between picking files and finishing, where the import's own buttons are in charge.
    public bool IsMidFlow => CurrentStep is ImportStep.Unlock or ImportStep.MapColumns or ImportStep.Preview or ImportStep.Review;

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string BusyText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsChooseFile => CurrentStep == ImportStep.ChooseFile;
    public bool IsUnlock => CurrentStep == ImportStep.Unlock;
    public bool IsMapColumns => CurrentStep == ImportStep.MapColumns;
    public bool IsPreview => CurrentStep == ImportStep.Preview;
    public bool IsReview => CurrentStep == ImportStep.Review;
    public bool IsDone => CurrentStep == ImportStep.Done;
    public bool IsFinished => IsDone;
    public int ImportedCount => _imported.Count;
    public bool HasMoreFiles => _queue.Count > 0;

    public string StepSentence => CurrentStep switch
    {
        ImportStep.ChooseFile => "Pick the statements you downloaded from your bank. CSV and PDF both work.",
        ImportStep.Unlock => "This PDF is locked. Banks usually use your ID number as the password.",
        ImportStep.MapColumns => "We couldn't recognise the columns, so tell us which one is which. We'll remember it next time.",
        ImportStep.Preview => "Check that these look right before we add them.",
        ImportStep.Review => "We've sorted each transaction into a category. Change any that look wrong.",
        _ => "All done. Your transactions are ready.",
    };

    [ObservableProperty] public partial string FileName { get; set; } = "";

    // Unlock
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    public partial string PasswordInput { get; set; } = "";

    [ObservableProperty] public partial string? PasswordError { get; set; }

    // Map columns
    [ObservableProperty] public partial IReadOnlyList<ColumnOption> ColumnOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ColumnOption> OptionalColumnOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> SampleLines { get; set; } = [];
    [ObservableProperty] public partial ColumnOption? DateColumn { get; set; }
    [ObservableProperty] public partial ColumnOption? DescriptionColumn { get; set; }
    [ObservableProperty] public partial ColumnOption? AmountColumn { get; set; }
    [ObservableProperty] public partial ColumnOption? DebitColumn { get; set; }
    [ObservableProperty] public partial ColumnOption? CreditColumn { get; set; }
    [ObservableProperty] public partial ColumnOption? BalanceColumn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseDebitCredit))]
    public partial bool UseSingleAmount { get; set; } = true;

    public bool UseDebitCredit
    {
        get => !UseSingleAmount;
        set => UseSingleAmount = !value;
    }

    // Preview
    [ObservableProperty] public partial string PreviewSummary { get; set; } = "";
    [ObservableProperty] public partial string DuplicateSummary { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<PreviewRow> PreviewRows { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<FileSummary> Files { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> Warnings { get; set; } = [];
    [ObservableProperty] public partial string ImportButtonText { get; set; } = "";
    [ObservableProperty] public partial bool CanImport { get; set; }

    // Review and done
    [ObservableProperty] public partial IReadOnlyList<TransactionRowViewModel> ReviewRows { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<CategoryCount> ReviewSummary { get; set; } = [];
    [ObservableProperty] public partial string DoneSummary { get; set; } = "";

    public bool HasDuplicates => !string.IsNullOrEmpty(DuplicateSummary);
    public bool HasWarnings => Warnings.Count > 0;

    [RelayCommand]
    private async Task ChooseFile()
    {
        ErrorMessage = null;
        var files = await _dialogs.PickStatementsAsync();
        if (files.Count > 0)
            await LoadFilesAsync(files);
    }

    // Shared by the file picker and drag-and-drop.
    public async Task LoadFilesAsync(IReadOnlyList<StatementFile> files)
    {
        if (IsBusy || files.Count == 0)
            return;

        ErrorMessage = null;
        _queue.Clear();
        _parsed.Clear();
        _failures.Clear();
        _needsUnlockStep = _needsMappingStep = false;
        foreach (var file in files)
            _queue.Enqueue(file);

        await ProcessQueueAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task Unlock()
    {
        if (_current is null)
            return;

        _current = _current with { Password = PasswordInput };
        PasswordInput = "";
        if (await ParseCurrentAsync(mapping: null))
            await ProcessQueueAsync();
    }

    private bool CanUnlock() => !string.IsNullOrEmpty(PasswordInput);

    [RelayCommand]
    private async Task SkipFile()
    {
        if (_current is not null)
            _failures.Add(Failed(_current.FileName, "Skipped"));
        _current = null;
        await ProcessQueueAsync();
    }

    [RelayCommand]
    private async Task ApplyMapping()
    {
        if (DateColumn?.Index is not { } date || DescriptionColumn?.Index is not { } description)
        {
            ErrorMessage = "Choose the date and description columns to continue.";
            return;
        }

        if (UseSingleAmount ? AmountColumn?.Index is null : DebitColumn?.Index is null && CreditColumn?.Index is null)
        {
            ErrorMessage = "Choose where the amounts are to continue.";
            return;
        }

        var mapping = (_detectedMapping ?? new ColumnMapping()) with
        {
            DateColumn = date,
            DescriptionColumn = description,
            AmountColumn = UseSingleAmount ? AmountColumn?.Index : null,
            DebitColumn = UseSingleAmount ? null : DebitColumn?.Index,
            CreditColumn = UseSingleAmount ? null : CreditColumn?.Index,
            BalanceColumn = BalanceColumn?.Index,
        };

        ErrorMessage = null;
        if (await ParseCurrentAsync(mapping))
            await ProcessQueueAsync();
    }

    [RelayCommand]
    private async Task ImportTransactions()
    {
        if (_preview is null)
            return;

        IsBusy = true;
        BusyText = "Adding your transactions…";
        try
        {
            _imported = _preview.NewTransactions.ToList();
            await _workspace.AddTransactionsAsync(_imported);
        }
        finally
        {
            IsBusy = false;
        }

        var categories = _workspace.Categories.Select(CategoryOption.From).ToList();
        ReviewRows = _imported
            .OrderBy(t => t.CategoryId == CategoryIds.Uncategorised ? 0 : 1)
            .ThenByDescending(t => t.Date)
            .Select(t => new TransactionRowViewModel(t, categories, _workspace.Settings.Currency, OnReviewCategoryChanged))
            .ToList();
        RefreshReviewSummary();
        GoTo(ImportStep.Review);
    }

    [RelayCommand]
    private void FinishReview()
    {
        var uncategorised = _imported.Count(t => t.CategoryId == CategoryIds.Uncategorised);
        DoneSummary = uncategorised > 0
            ? $"{Display.Count(_imported.Count, "transaction")} added. {Display.Count(uncategorised, "is", "are")} still uncategorised; you can sort them on the Transactions page."
            : $"{Display.Count(_imported.Count, "transaction")} added and sorted into categories.";
        GoTo(ImportStep.Done);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back()
    {
        ErrorMessage = null;
        _queue.Clear();
        GoTo(ImportStep.ChooseFile);
    }

    [RelayCommand]
    private void ViewTransactions() => _navigator.GoTo(Page.Transactions);

    [RelayCommand]
    public void Reset()
    {
        _queue.Clear();
        _parsed.Clear();
        _failures.Clear();
        _current = null;
        _preview = null;
        _detectedMapping = null;
        _imported = [];
        _needsUnlockStep = _needsMappingStep = false;
        ErrorMessage = null;
        FileName = "";
        BuildSteps();
        GoTo(ImportStep.ChooseFile);
    }

    private async Task ProcessQueueAsync()
    {
        while (_queue.Count > 0)
        {
            _current = _queue.Dequeue();
            FileName = _current.FileName;
            if (!await ParseCurrentAsync(mapping: null))
                return;
        }

        _current = null;
        OnPropertyChanged(nameof(HasMoreFiles));

        if (_parsed.Count == 0)
        {
            ErrorMessage = _failures.Count == 1
                ? _failures[0].StatusText
                : "None of those files could be read. " + string.Join(" ", _failures.Select(f => $"{f.FileName}: {f.StatusText}"));
            BuildSteps();
            GoTo(ImportStep.ChooseFile);
            return;
        }

        ShowPreview();
    }

    // Returns true when the current file is done (read or given up on) and the queue can move on;
    // false when we're waiting on the user for a password or column mapping.
    private async Task<bool> ParseCurrentAsync(ColumnMapping? mapping)
    {
        if (_current is not { } file)
            return true;

        IsBusy = true;
        BusyText = file.LooksLikePdf ? $"Reading {file.FileName}…" : $"Opening {file.FileName}…";
        ParseResult result;
        try
        {
            result = await Task.Run(() => _importer.Parse(file, mapping));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            result = ParseResult.Failed($"We couldn't read this file: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        switch (result.Status)
        {
            case ParseStatus.Success:
                _parsed.Add((file, result));
                if (mapping is not null && _currentSignature is not null)
                {
                    _workspace.Settings.SavedColumnMappings[_currentSignature] = mapping;
                    await _workspace.SaveSettingsAsync();
                    _toasts.Show("Got it. We'll recognise files laid out like this next time.");
                }

                _currentSignature = null;
                PasswordError = null;
                return true;

            case ParseStatus.NeedsPassword:
                PasswordError = file.Password is null ? null : result.Message;
                _needsUnlockStep = true;
                BuildSteps();
                GoTo(ImportStep.Unlock);
                OnPropertyChanged(nameof(HasMoreFiles));
                return false;

            case ParseStatus.NeedsMapping:
                var signature = Signature(result.Headers);
                if (mapping is null && _workspace.Settings.SavedColumnMappings.TryGetValue(signature, out var saved))
                {
                    _currentSignature = null;
                    var retry = await Task.Run(() => _importer.Parse(file, saved));
                    if (retry.Status == ParseStatus.Success)
                    {
                        _parsed.Add((file, retry));
                        return true;
                    }
                }

                _currentSignature = signature;
                PrepareMapping(result);
                if (mapping is not null)
                    ErrorMessage = result.Message;
                _needsMappingStep = true;
                BuildSteps();
                GoTo(ImportStep.MapColumns);
                OnPropertyChanged(nameof(HasMoreFiles));
                return false;

            default:
                _failures.Add(Failed(file.FileName, result.Message ?? "We couldn't read this file."));
                return true;
        }
    }

    private void PrepareMapping(ParseResult result)
    {
        _detectedMapping = result.Mapping;
        ColumnOptions = result.Headers.Select((h, i) => new ColumnOption(i, h)).ToList();
        OptionalColumnOptions = [new ColumnOption(null, "Not in this file"), .. ColumnOptions];
        SampleLines = result.SampleRows.Select(r => string.Join("   ·   ", r)).ToList();

        var m = result.Mapping;
        DateColumn = Find(m?.DateColumn);
        DescriptionColumn = Find(m?.DescriptionColumn);
        AmountColumn = Find(m?.AmountColumn);
        DebitColumn = FindOptional(m?.DebitColumn);
        CreditColumn = FindOptional(m?.CreditColumn);
        BalanceColumn = FindOptional(m?.BalanceColumn);
        UseSingleAmount = m?.DebitColumn is null && m?.CreditColumn is null;

        ColumnOption? Find(int? index) => ColumnOptions.FirstOrDefault(c => c.Index == index);
        ColumnOption FindOptional(int? index) => OptionalColumnOptions.FirstOrDefault(c => c.Index == index) ?? OptionalColumnOptions[0];
    }

    // Each file is hashed on its own (so repeated identical rows keep their occurrence numbers),
    // then checked against both the saved data and the files earlier in this batch.
    private void ShowPreview()
    {
        var currency = _workspace.Settings.Currency;
        var sessionHashes = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<ImportCandidate>();
        var summaries = new List<FileSummary>();

        foreach (var (file, result) in _parsed)
        {
            var preview = _importer.BuildPreview(result.Transactions, h => _workspace.ContainsHash(h) || sessionHashes.Contains(h),
                _workspace.Categoriser, _workspace.DefaultAccount.Id);
            foreach (var item in preview.Items.Where(i => !i.IsDuplicate))
                sessionHashes.Add(item.Transaction.ImportHash);
            items.AddRange(preview.Items);
            summaries.Add(Summarise(file, result, preview));
        }

        _preview = new ImportPreview(items.OrderBy(i => i.Transaction.Date).ToList());
        Files = [.. summaries, .. _failures];
        Warnings = _parsed.SelectMany(p => p.Result.Warnings).Take(5).ToList();
        OnPropertyChanged(nameof(HasWarnings));

        PreviewRows = _preview.Items
            .Take(PreviewLimit)
            .Select(i => new PreviewRow(
                Display.Date(i.Transaction.Date),
                i.Transaction.CleanDescription,
                MoneyFormatter.Format(i.Transaction.Amount, currency),
                i.Transaction.Amount > 0,
                i.IsDuplicate))
            .ToList();

        var range = _preview.From is { } from && _preview.To is { } to ? $" from {Display.Date(from)} to {Display.Date(to)}" : "";
        var fileCount = _parsed.Count > 1 ? $" across {_parsed.Count} files" : "";
        PreviewSummary = $"We found {Display.Count(_preview.Items.Count, "transaction")}{range}{fileCount}.";
        if (_preview.Items.Count > PreviewLimit)
            PreviewSummary += $" Showing the first {PreviewLimit}.";

        DuplicateSummary = _preview.DuplicateCount > 0
            ? $"{Display.Count(_preview.DuplicateCount, "transaction")} {(_preview.DuplicateCount == 1 ? "was" : "were")} already imported and will be skipped."
            : "";
        OnPropertyChanged(nameof(HasDuplicates));

        CanImport = _preview.NewCount > 0;
        ImportButtonText = CanImport ? $"Import {Display.Count(_preview.NewCount, "transaction")}" : "Nothing new to import";
        GoTo(ImportStep.Preview);
    }

    private static FileSummary Summarise(StatementFile file, ParseResult result, ImportPreview preview)
    {
        var range = preview.From is { } from && preview.To is { } to ? $" · {Display.Date(from)} to {Display.Date(to)}" : "";
        var detail = $"{Display.Count(preview.Items.Count, "transaction")}{range}";
        var (status, good, warning) = result.BalanceCheck switch
        {
            { AllGood: true } => ("Balances check out", true, false),
            { Mismatches: > 0 } check => ($"{Display.Count(check.Mismatches, "row")} to double-check", false, true),
            _ => (file.LooksLikePdf ? "Read from PDF" : "Read from CSV", false, false),
        };
        return new FileSummary(file.FileName, file.LooksLikePdf ? "file" : "list", detail, result.BankName, status, good, warning, false);
    }

    private static FileSummary Failed(string fileName, string reason) =>
        new(fileName, "alert", reason, null, "Not imported", false, false, true);

    private static string Signature(IReadOnlyList<string> headers) =>
        string.Join('|', headers.Select(h => h.Trim().ToLowerInvariant()));

    private async Task OnReviewCategoryChanged(TransactionRowViewModel row, CategoryOption category)
    {
        await _workspace.SetCategoryAsync(row.Transaction, category.Id);
        RefreshReviewSummary();
    }

    private void RefreshReviewSummary()
    {
        ReviewSummary = _imported
            .GroupBy(t => t.CategoryId)
            .Select(g => (Category: _workspace.FindCategory(g.Key), Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Select(x => new CategoryCount(x.Category?.Name ?? "Uncategorised", x.Category?.Colour ?? "chart-8", x.Count.ToString("#,0")))
            .ToList();
    }

    private void BuildSteps()
    {
        var steps = new List<(ImportStep Step, string Title)> { (ImportStep.ChooseFile, "Choose files") };
        if (_needsUnlockStep)
            steps.Add((ImportStep.Unlock, "Unlock"));
        if (_needsMappingStep)
            steps.Add((ImportStep.MapColumns, "Map columns"));
        steps.Add((ImportStep.Preview, "Preview"));
        steps.Add((ImportStep.Review, "Review categories"));
        steps.Add((ImportStep.Done, "Done"));

        Steps.Clear();
        for (var i = 0; i < steps.Count; i++)
            Steps.Add(new StepItem(steps[i].Step, i + 1, steps[i].Title));
        UpdateStepStates();
    }

    private void GoTo(ImportStep step)
    {
        CurrentStep = step;
        UpdateStepStates();
    }

    private void UpdateStepStates()
    {
        foreach (var item in Steps)
        {
            item.IsCurrent = item.Step == CurrentStep;
            item.IsDone = item.Step < CurrentStep || CurrentStep == ImportStep.Done;
        }
    }
}
