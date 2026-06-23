using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiiiAssist.FireflyIII;
using FiiiAssist.Models;
using FiiiAssist.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FiiiAssist.ViewModels;

/// <summary>
/// ViewModel for the Duplicate Transaction Checker page.
/// Guides the user through selecting a date range and account,
/// then loads all transactions and flags potential duplicates
/// (same amount on the same day).
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public partial class DuplicateCheckerViewModel : ObservableObject
{
    private readonly FireflyIIIService _fireflyService;

    // ── Observable state ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingAccounts;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _totalTransactionCount;

    [ObservableProperty]
    private int _duplicateCount;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _deleteStatusMessage = string.Empty;

    // ── Date range (optional — when disabled, all history is loaded) ─────────

    [ObservableProperty]
    private bool _useStartDate;

    [ObservableProperty]
    private bool _useEndDate;

    [ObservableProperty]
    private DateTimeOffset _startDate = DateTimeOffset.Now.AddMonths(-1);

    [ObservableProperty]
    private DateTimeOffset _endDate = DateTimeOffset.Now;

    // ── Account selection ─────────────────────────────────────────────────────

    public ObservableCollection<AccountRead> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    private AccountRead? _selectedAccount;

    public bool CanSearch => SelectedAccount != null && !IsLoading && !IsDeleting;

    public bool CanDelete => SelectedCount > 0 && !IsDeleting && !IsLoading;

    // ── Results ───────────────────────────────────────────────────────────────

    public ObservableCollection<DuplicateTransactionRow> Transactions { get; } = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public DuplicateCheckerViewModel(FireflyIIIService fireflyService)
    {
        _fireflyService = fireflyService;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAccountsAsync()
    {
        IsLoadingAccounts = true;
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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load accounts: {ex.Message}";
        }
        finally
        {
            IsLoadingAccounts = false;
        }
    }

    [RelayCommand]
    private async Task SearchDuplicatesAsync()
    {
        if (SelectedAccount is null)
            return;

        IsLoading = true;
        ErrorMessage = string.Empty;
        DeleteStatusMessage = string.Empty;
        HasResults = false;
        Transactions.Clear();
        TotalTransactionCount = 0;
        DuplicateCount = 0;
        SelectedCount = 0;

        try
        {
            var accountId = SelectedAccount.Id;
            DateOnly? startDate = UseStartDate ? DateOnly.FromDateTime(StartDate.DateTime) : null;
            DateOnly? endDate = UseEndDate ? DateOnly.FromDateTime(EndDate.DateTime) : null;

            // Fetch all transactions for the account in the date range
            var allTransactions = await FetchTransactionsAsync(accountId, startDate, endDate);

            // Group by date + amount to find duplicates — only keep groups with > 1 entry
            var duplicateRows = allTransactions
                .GroupBy(t => new { t.Date, t.Amount })
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToList();

            // Mark all as duplicates and sort by date descending
            var rows = duplicateRows
                .Select(t =>
                {
                    t.IsDuplicate = true;
                    return t;
                })
                .OrderByDescending(t => t.Date)
                .ThenBy(t => t.Amount)
                .ThenBy(t => t.Description)
                .ToList();

            foreach (var row in rows)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DuplicateTransactionRow.IsSelected))
                    {
                        UpdateSelectedCount();
                        UpdateKeptIndicators();
                    }
                };
                Transactions.Add(row);
            }

            TotalTransactionCount = rows.Count;
            DuplicateCount = rows.Count;
            HasResults = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading transactions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(CanDelete));
        }
    }

    /// <summary>
    /// Selects potential duplicates for deletion. Within each duplicate group (same date + amount),
    /// selects transactions whose ExternalId is a GUID (e.g. "2290735e-e308-4030-9e94-af9cc347ef5a").
    /// These are typically auto-generated IDs from import tools, whereas the "real" transaction
    /// has a bank-assigned FitId.
    /// </summary>
    [RelayCommand]
    private void SelectDuplicates()
    {
        // Clear all selections first
        foreach (var row in Transactions)
            row.IsSelected = false;

        // Group duplicates by date + amount
        var duplicateGroups = Transactions
            .GroupBy(t => new { t.Date, t.Amount })
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var items = group.ToList();
            if (items.Count < 2)
                continue;

            // Find items with GUID-style external IDs
            var guidItems = items.Where(t => IsGuidExternalId(t.ExternalId)).ToList();

            if (guidItems.Count > 0 && guidItems.Count < items.Count)
            {
                // Select the GUID ones for deletion (keep the non-GUID ones)
                foreach (var item in guidItems)
                    item.IsSelected = true;
            }
            else if (guidItems.Count == items.Count || guidItems.Count == 0)
            {
                // All or none have GUIDs — select all but the first (keep one)
                foreach (var item in items.Skip(1))
                    item.IsSelected = true;
            }
        }

        UpdateSelectedCount();
        UpdateKeptIndicators();
    }

    /// <summary>
    /// Deletes all selected transactions from Firefly III.
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var toDelete = Transactions.Where(t => t.IsSelected).ToList();
        if (toDelete.Count == 0)
            return;

        IsDeleting = true;
        ErrorMessage = string.Empty;
        DeleteStatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanDelete));

        var client = _fireflyService.Client;
        if (client is null)
        {
            ErrorMessage = "Not connected to Firefly III.";
            IsDeleting = false;
            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(CanDelete));
            return;
        }

        int deleted = 0;
        int failed = 0;

        foreach (var row in toDelete)
        {
            try
            {
                await client.DeleteTransactionAsync(null, row.TransactionId);
                deleted++;
                Transactions.Remove(row);
            }
            catch
            {
                failed++;
            }
        }

        // Update counts
        TotalTransactionCount = Transactions.Count;
        DuplicateCount = Transactions.Count(r => r.IsDuplicate);
        UpdateSelectedCount();

        IsDeleting = false;
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanDelete));

        DeleteStatusMessage = failed == 0
            ? $"✔ Successfully deleted {deleted} transaction(s)."
            : $"Deleted {deleted} transaction(s). {failed} failed.";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static readonly Regex GuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsGuidExternalId(string externalId)
    {
        return !string.IsNullOrWhiteSpace(externalId) && GuidPattern.IsMatch(externalId.Trim());
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Transactions.Count(t => t.IsSelected);
        OnPropertyChanged(nameof(CanDelete));
    }

    /// <summary>
    /// Updates the IsKept indicator for each row. A row is "kept" when it is NOT selected
    /// but at least one other row in its duplicate group IS selected for deletion.
    /// This gives the user visual feedback about which transaction will be preserved.
    /// </summary>
    private void UpdateKeptIndicators()
    {
        // Group by date + amount
        var groups = Transactions
            .GroupBy(t => new { t.Date, t.Amount })
            .ToList();

        foreach (var group in groups)
        {
            var items = group.ToList();
            bool anySelected = items.Any(t => t.IsSelected);

            foreach (var item in items)
            {
                // A row is "kept" if it's not selected and at least one sibling is selected
                item.IsKept = !item.IsSelected && anySelected;
            }
        }
    }

    private async Task<List<DuplicateTransactionRow>> FetchTransactionsAsync(
        string accountId, DateOnly? startDate, DateOnly? endDate)
    {
        var results = new List<DuplicateTransactionRow>();
        var client = _fireflyService.Client;

        if (client is null)
            throw new InvalidOperationException("Not connected to Firefly III.");

        DateTimeOffset? start = startDate.HasValue
            ? new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        DateTimeOffset? end = endDate.HasValue
            ? new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
            : null;

        var page = 1;
        const int pageSize = 100;

        while (true)
        {
            var result = await client.ListTransactionByAccountAsync(
                x_Trace_Id: null,
                limit: pageSize,
                page: page,
                id: accountId,
                start: start,
                end: end,
                type: TransactionTypeFilter.All);

            if (result?.Data == null || result.Data.Count == 0)
                break;

            foreach (var txGroup in result.Data)
            {
                if (txGroup?.Attributes?.Transactions == null)
                    continue;

                foreach (var split in txGroup.Attributes.Transactions)
                {
                    var amount = decimal.TryParse(split.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        ? a
                        : 0m;

                    // For withdrawals the amount is negative from the account's perspective
                    if (split.Type == TransactionTypeProperty.Withdrawal)
                        amount = -Math.Abs(amount);
                    else if (split.Type == TransactionTypeProperty.Deposit)
                        amount = Math.Abs(amount);

                    results.Add(new DuplicateTransactionRow
                    {
                        Date = DateOnly.FromDateTime(split.Date.DateTime),
                        Description = split.Description ?? string.Empty,
                        Amount = amount,
                        SourceName = split.Source_name ?? string.Empty,
                        DestinationName = split.Destination_name ?? string.Empty,
                        TransactionType = split.Type.ToString(),
                        ExternalId = split.External_id ?? string.Empty,
                        TransactionId = txGroup.Id ?? string.Empty,
                    });
                }
            }

            if (result.Data.Count < pageSize)
                break;

            page++;
        }

        return results;
    }
}
