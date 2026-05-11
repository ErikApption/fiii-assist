using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Collections.ObjectModel;
using System.Net.Http;

namespace QfxWatcher.ViewModels;

/// <summary>
/// ViewModel managing the multi-step QFX Import Wizard.
/// Guides the user through account selection, file picking,
/// transaction preview, and import execution.
/// </summary>
public partial class ImportWizardViewModel : ObservableObject
{
    private readonly FireflyIIIService _fireflyService;
    private readonly SettingsService   _settings;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWizardOpen))]
    [NotifyPropertyChangedFor(nameof(CanOpenWizard))]
    private WizardStep _currentStep = WizardStep.Closed;

    /// <summary>True when the wizard is active (not Closed).</summary>
    public bool IsWizardOpen => CurrentStep != WizardStep.Closed;

    /// <summary>True when the wizard can be opened (currently closed). Used to enable the Import button.</summary>
    public bool CanOpenWizard => CurrentStep == WizardStep.Closed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canGoBack;

    // ── Account selection ─────────────────────────────────────────────────────

    public ObservableCollection<AccountRead> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAccountName))]
    private AccountRead? _selectedAccount;

    /// <summary>Display name of the currently selected account (for UI binding).</summary>
    public string SelectedAccountName => SelectedAccount?.Attributes?.Name ?? string.Empty;

    // ── File & transactions ───────────────────────────────────────────────────

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private string _selectedFileName = string.Empty;

    public ObservableCollection<FIIITransaction> Transactions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransactionListEmpty))]
    private int _transactionCount;

    /// <summary>True when no transactions were parsed (for UI warning binding).</summary>
    public bool IsTransactionListEmpty => TransactionCount == 0;

    // ── Import progress ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedCount))]
    private int _importedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessedCount))]
    private int _failedCount;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// Gets the total number of transactions processed so far (imported + failed).
    /// Used by the UI to display progress during import.
    /// </summary>
    public int ProcessedCount => ImportedCount + FailedCount;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when an import operation completes (success or partial failure).
    /// Subscribers (e.g. DashboardViewModel) can use this to add a log entry.
    /// </summary>
    public event EventHandler<ImportLogEntry>? ImportCompleted;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ImportWizardViewModel(
        FireflyIIIService fireflyService,
        SettingsService   settings)
    {
        _fireflyService = fireflyService;
        _settings       = settings;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenWizardAsync()
    {
        CurrentStep = WizardStep.AccountSelection;
        await LoadAccountsAsync();
    }

    [RelayCommand]
    private async Task RetryLoadAccountsAsync()
    {
        await LoadAccountsAsync();
    }

    [RelayCommand]
    private void SelectAccount(AccountRead? account)
    {
        SelectedAccount = account;
        CanGoNext = account != null;
    }

    [RelayCommand]
    private void ProceedToFileSelection()
    {
        CurrentStep = WizardStep.FileSelection;
        CanGoBack = false;
    }

    /// <summary>
    /// Called by the View when the user selects a file from the FilePicker.
    /// Parses the file and transitions to TransactionPreview on success,
    /// or sets an error and returns to FileSelection on failure.
    /// </summary>
    public void FileSelected(string path)
    {
        ErrorMessage = string.Empty;
        SelectedFilePath = path;
        SelectedFileName = System.IO.Path.GetFileName(path);

        try
        {
            var parsed = QfxParserService.ParseFile(path);

            Transactions.Clear();
            foreach (var tx in parsed.OrderByDescending(t => t.Date))
            {
                Transactions.Add(tx);
            }

            TransactionCount = Transactions.Count;

            // Disable import action when zero transactions found
            CanGoNext = TransactionCount > 0;

            CurrentStep = WizardStep.TransactionPreview;
            CanGoBack = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not parse file: {ex.Message}";
            Transactions.Clear();
            TransactionCount = 0;
            CanGoNext = false;
            CurrentStep = WizardStep.FileSelection;
        }
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (SelectedAccount is null || Transactions.Count == 0)
            return;

        // Transition to Importing step and disable navigation
        CurrentStep = WizardStep.Importing;
        TotalCount = Transactions.Count;
        ImportedCount = 0;
        FailedCount = 0;
        CanGoBack = false;
        CanGoNext = false;

        var settings = _settings.Load();
        var accountId = SelectedAccount.Id;

        // Import each transaction individually so failures don't block remaining ones
        foreach (var tx in Transactions)
        {
            try
            {
                await _fireflyService.ImportTransactionsAsync(
                    accountId,
                    [tx],
                    settings.ErrorIfDuplicateHash);
                ImportedCount++;
            }
            catch
            {
                FailedCount++;
            }
        }

        // Transition to Results step
        CurrentStep = WizardStep.Results;

        // Raise ImportCompleted event so DashboardViewModel can add a log entry
        var logEntry = new ImportLogEntry
        {
            FileName = SelectedFileName,
            AccountName = SelectedAccount.Attributes.Name,
            TransactionCount = ImportedCount,
            Success = FailedCount == 0,
            ErrorMessage = FailedCount > 0
                ? $"{FailedCount} transaction(s) failed to import"
                : string.Empty,
        };
        ImportCompleted?.Invoke(this, logEntry);
    }

    [RelayCommand]
    private void GoBack()
    {
        switch (CurrentStep)
        {
            case WizardStep.TransactionPreview:
                Transactions.Clear();
                TransactionCount = 0;
                CurrentStep = WizardStep.AccountSelection;
                CanGoBack = false;
                break;

            case WizardStep.FileSelection:
                CurrentStep = WizardStep.AccountSelection;
                CanGoBack = false;
                break;
        }
        // SelectedAccount is preserved — no clearing here
    }

    [RelayCommand]
    private void Close()
    {
        CurrentStep = WizardStep.Closed;
        Accounts.Clear();
        Transactions.Clear();
        SelectedAccount = null;
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        ErrorMessage = string.Empty;
        IsLoading = false;
        CanGoNext = false;
        CanGoBack = false;
        ImportedCount = 0;
        FailedCount = 0;
        TotalCount = 0;
        TransactionCount = 0;
    }

    [RelayCommand]
    private void ImportAnother()
    {
        Transactions.Clear();
        TransactionCount = 0;
        ImportedCount = 0;
        FailedCount = 0;
        TotalCount = 0;
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        ErrorMessage = string.Empty;
        CurrentStep = WizardStep.FileSelection;
        CanGoBack = false;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Shared method that fetches accounts from Firefly III, populates the Accounts collection,
    /// and pre-selects the default account if configured.
    /// </summary>
    private async Task LoadAccountsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var accountSingles = await _fireflyService.GetAccountsAsync()
                .WaitAsync(cts.Token);

            Accounts.Clear();
            foreach (var accountSingle in accountSingles)
            {
                Accounts.Add(accountSingle.Data);
            }

            // Pre-select account matching DefaultAccountId from settings
            var settings = _settings.Load();
            if (!string.IsNullOrEmpty(settings.DefaultAccountId))
            {
                var defaultAccount = Accounts.FirstOrDefault(
                    a => a.Id == settings.DefaultAccountId);
                if (defaultAccount is not null)
                {
                    SelectedAccount = defaultAccount;
                    CanGoNext = true;
                }
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Unable to connect to Firefly III. Please check your network connection and server settings.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "The request timed out. Please check your network connection and try again.";
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "The request timed out. Please check your network connection and try again.";
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            ErrorMessage = "Authentication failed — check your token in Settings.";
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred while loading accounts. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
