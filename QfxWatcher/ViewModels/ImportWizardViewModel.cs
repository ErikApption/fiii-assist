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
    private bool _accountAutoMatched;
    private string _qfxAccountId = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(ProcessedCount))]
    private int _skippedCount;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// Gets the total number of transactions processed so far (imported + failed + skipped).
    /// Used by the UI to display progress during import.
    /// </summary>
    public int ProcessedCount => ImportedCount + FailedCount + SkippedCount;

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
    private void OpenWizard()
    {
        CurrentStep = WizardStep.FileSelection;
        CanGoBack = false;
        CanGoNext = false;
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
    private void ProceedToTransactionPreview()
    {
        if (SelectedAccount is null)
            return;

        CurrentStep = WizardStep.TransactionPreview;
        CanGoBack = true;
        CanGoNext = TransactionCount > 0;
    }

    /// <summary>
    /// Called by the View when the user selects a file from the FilePicker.
    /// Parses the file, extracts ACCTID, loads accounts from Firefly III,
    /// and auto-matches the account if possible. Skips account selection
    /// when a match is found.
    /// </summary>
    public async Task FileSelectedAsync(string path)
    {
        ErrorMessage = string.Empty;
        SelectedFilePath = path;
        SelectedFileName = System.IO.Path.GetFileName(path);
        _accountAutoMatched = false;

        try
        {
            var parsed = QfxParserService.ParseFile(path);

            Transactions.Clear();
            foreach (var tx in parsed.OrderByDescending(t => t.Date))
            {
                Transactions.Add(tx);
            }

            TransactionCount = Transactions.Count;

            // Extract the ACCTID from the QFX file header
            _qfxAccountId = QfxParserService.ExtractAccountId(path);

            // Load accounts from Firefly III to attempt auto-matching
            await LoadAccountsAsync();

            if (!string.IsNullOrWhiteSpace(ErrorMessage))
            {
                // Failed to load accounts — show account selection step with the error
                CurrentStep = WizardStep.AccountSelection;
                CanGoBack = true;
                return;
            }

            // Try to auto-match using the ACCTID from the QFX file
            AccountRead? matchedAccount = null;
            if (!string.IsNullOrWhiteSpace(_qfxAccountId))
            {
                matchedAccount = Accounts.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.Attributes?.Account_number) &&
                    a.Attributes.Account_number.Trim().Equals(_qfxAccountId.Trim(), StringComparison.OrdinalIgnoreCase));

                // Also try suffix match (some banks truncate account numbers in QFX)
                matchedAccount ??= Accounts.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.Attributes?.Account_number) &&
                    _qfxAccountId.Trim().Length >= 4 &&
                    a.Attributes.Account_number.Trim().EndsWith(_qfxAccountId.Trim(), StringComparison.OrdinalIgnoreCase));

                // Try IBAN match
                matchedAccount ??= Accounts.FirstOrDefault(a =>
                    !string.IsNullOrWhiteSpace(a.Attributes?.Iban) &&
                    (a.Attributes.Iban.Trim().Equals(_qfxAccountId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     a.Attributes.Iban.Trim().EndsWith(_qfxAccountId.Trim(), StringComparison.OrdinalIgnoreCase)));
            }

            if (matchedAccount is not null)
            {
                // Auto-matched — skip account selection
                SelectedAccount = matchedAccount;
                _accountAutoMatched = true;
                CanGoNext = TransactionCount > 0;
                CurrentStep = WizardStep.TransactionPreview;
                CanGoBack = true;
            }
            else
            {
                // No match — user must select an account
                CanGoNext = SelectedAccount != null;
                CurrentStep = WizardStep.AccountSelection;
                CanGoBack = true;
            }
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
        SkippedCount = 0;
        CanGoBack = false;
        CanGoNext = false;

        var settings = _settings.Load();
        var accountId = SelectedAccount.Id;

        // If the user manually selected the account and the QFX file had an ACCTID,
        // update the Firefly III account's account_number so future imports auto-match.
        if (!_accountAutoMatched && !string.IsNullOrWhiteSpace(_qfxAccountId))
        {
            var existingNumber = SelectedAccount.Attributes?.Account_number?.Trim();
            if (string.IsNullOrWhiteSpace(existingNumber) ||
                !existingNumber.Equals(_qfxAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _fireflyService.UpdateAccountNumberAsync(accountId, _qfxAccountId.Trim());
                }
                catch
                {
                    // Non-fatal — the import can still proceed without the account number update
                }
            }
        }

        // Build set of existing external IDs for duplicate detection
        HashSet<string> existingIds = [];
        if (settings.SkipDuplicateTransactions)
        {
            try
            {
                var dates = Transactions.Select(t => t.Date).ToList();
                var minDate = dates.Min();
                var maxDate = dates.Max();
                existingIds = await _fireflyService.GetExistingExternalIdsAsync(accountId, minDate, maxDate);
            }
            catch
            {
                // If lookup fails, proceed without skipping — server-side dedup still applies
            }
        }

        // Import each transaction individually so failures don't block remaining ones
        foreach (var tx in Transactions)
        {
            // Skip if this transaction's FitId already exists in Firefly III
            if (settings.SkipDuplicateTransactions
                && !string.IsNullOrWhiteSpace(tx.FitId)
                && existingIds.Contains(tx.FitId))
            {
                SkippedCount++;
                continue;
            }

            try
            {
                await _fireflyService.ImportTransactionsAsync(
                    accountId,
                    [tx],
                    settings.ErrorIfDuplicateHash,
                    settings.SkipDuplicatesByContent);
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
            SkippedCount = SkippedCount,
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
                if (_accountAutoMatched)
                {
                    // Account was auto-matched — go back to file selection
                    Transactions.Clear();
                    TransactionCount = 0;
                    SelectedAccount = null;
                    _accountAutoMatched = false;
                    CurrentStep = WizardStep.FileSelection;
                    CanGoBack = false;
                }
                else
                {
                    // User selected the account — go back to account selection
                    CurrentStep = WizardStep.AccountSelection;
                    CanGoNext = SelectedAccount != null;
                    CanGoBack = true;
                }
                break;

            case WizardStep.AccountSelection:
                // Go back to file selection
                Transactions.Clear();
                TransactionCount = 0;
                SelectedAccount = null;
                CurrentStep = WizardStep.FileSelection;
                CanGoBack = false;
                break;
        }
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
        SkippedCount = 0;
        TotalCount = 0;
        TransactionCount = 0;
        _accountAutoMatched = false;
        _qfxAccountId = string.Empty;
    }

    [RelayCommand]
    private void ImportAnother()
    {
        Transactions.Clear();
        TransactionCount = 0;
        ImportedCount = 0;
        FailedCount = 0;
        SkippedCount = 0;
        TotalCount = 0;
        SelectedFilePath = string.Empty;
        SelectedFileName = string.Empty;
        SelectedAccount = null;
        ErrorMessage = string.Empty;
        _accountAutoMatched = false;
        _qfxAccountId = string.Empty;
        CurrentStep = WizardStep.FileSelection;
        CanGoBack = false;
        CanGoNext = false;
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
