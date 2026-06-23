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
using System.Threading;
using System.Threading.Tasks;

namespace FiiiAssist.ViewModels;

/// <summary>
/// ViewModel for the Subscription Detector page.
/// Loads transactions for a selected account and identifies recurring patterns:
/// same payee with amounts within ±10%, ignoring transactions already linked to a subscription.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public partial class SubscriptionDetectorViewModel : ObservableObject
{
    private readonly FireflyIIIService _fireflyService;
    private readonly IgnoredSubscriptionService _ignoredService;

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
    private int _detectedCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Minimum occurrences filter ────────────────────────────────────────────

    [ObservableProperty]
    private int _minimumOccurrences = 3;

    // ── Amount tolerance ──────────────────────────────────────────────────────

    /// <summary>
    /// Percentage tolerance for grouping similar amounts (e.g. 10 means ±10%).
    /// Also used as the buffer when creating subscriptions.
    /// </summary>
    [ObservableProperty]
    private int _amountTolerancePercent = 10;

    // ── Account selection ─────────────────────────────────────────────────────

    public ObservableCollection<AccountRead> Accounts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSearch))]
    private AccountRead? _selectedAccount;

    public bool CanSearch => SelectedAccount != null && !IsLoading;

    // ── Results ───────────────────────────────────────────────────────────────

    public ObservableCollection<DetectedSubscriptionRow> DetectedSubscriptions { get; } = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public SubscriptionDetectorViewModel(FireflyIIIService fireflyService, IgnoredSubscriptionService ignoredService)
    {
        _fireflyService = fireflyService;
        _ignoredService = ignoredService;
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
    private async Task DetectSubscriptionsAsync()
    {
        if (SelectedAccount is null)
            return;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        HasResults = false;
        DetectedSubscriptions.Clear();
        DetectedCount = 0;

        try
        {
            var accountId = SelectedAccount.Id;

            // Fetch all transactions for the account (no date filter — we want full history)
            var allTransactions = await FetchTransactionsAsync(accountId);

            StatusMessage = $"Loaded {allTransactions.Count} transactions. Analyzing patterns…";

            // Filter out transactions that are already linked to a subscription/bill
            var unlinkedTransactions = allTransactions
                .Where(t => string.IsNullOrWhiteSpace(t.BillId))
                .ToList();

            // Group by payee name (case-insensitive)
            var payeeGroups = unlinkedTransactions
                .Where(t => !string.IsNullOrWhiteSpace(t.PayeeName))
                .GroupBy(t => t.PayeeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= MinimumOccurrences)
                .ToList();

            var detected = new List<DetectedSubscriptionRow>();

            foreach (var group in payeeGroups)
            {
                // Within each payee group, find clusters of similar amounts (±tolerance%)
                var amountClusters = FindAmountClusters(group.ToList(), AmountTolerancePercent / 100m);

                foreach (var cluster in amountClusters)
                {
                    if (cluster.Count < MinimumOccurrences)
                        continue;

                    var dates = cluster.Select(t => t.Date).OrderBy(d => d).ToList();

                    // At least one payment must be within the last year to be considered active
                    var oneYearAgo = DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
                    if (!dates.Any(d => d >= oneYearAgo))
                        continue;

                    var amounts = cluster.Select(t => Math.Abs(t.Amount)).ToList();
                    var avgAmount = amounts.Average();

                    // Skip if this pattern is in the ignore list
                    if (_ignoredService.IsIgnored(group.Key, avgAmount))
                        continue;

                    var avgInterval = CalculateAverageInterval(dates);
                    var suggestedPeriod = SuggestPeriod(avgInterval);

                    detected.Add(new DetectedSubscriptionRow
                    {
                        PayeeName = group.Key,
                        AverageAmount = avgAmount,
                        MinAmount = amounts.Min(),
                        MaxAmount = amounts.Max(),
                        TransactionCount = cluster.Count,
                        SuggestedPeriod = suggestedPeriod,
                        SuggestedFrequency = SuggestFrequency(avgInterval),
                        AverageIntervalDays = avgInterval,
                        TransactionDates = dates,
                    });
                }
            }

            // Sort by transaction count descending, then by payee name
            var sorted = detected
                .OrderByDescending(d => d.TransactionCount)
                .ThenBy(d => d.PayeeName)
                .ToList();

            foreach (var row in sorted)
            {
                DetectedSubscriptions.Add(row);
            }

            DetectedCount = sorted.Count;
            HasResults = true;
            StatusMessage = $"Found {sorted.Count} potential subscription(s) from {unlinkedTransactions.Count} unlinked transactions (ignored {allTransactions.Count - unlinkedTransactions.Count} already linked).";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error analyzing transactions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    /// <summary>
    /// Ignores a detected subscription pattern so it won't appear in future scans.
    /// </summary>
    [RelayCommand]
    private void IgnoreSubscription(DetectedSubscriptionRow row)
    {
        if (row is null) return;

        _ignoredService.AddIgnored(row.PayeeName, row.AverageAmount);
        DetectedSubscriptions.Remove(row);
        DetectedCount = DetectedSubscriptions.Count;
    }

    /// <summary>
    /// Ignores all subscription patterns for a payee, regardless of amount.
    /// Removes all rows with the same payee name from the current results.
    /// </summary>
    [RelayCommand]
    private void IgnorePayee(DetectedSubscriptionRow row)
    {
        if (row is null) return;

        _ignoredService.AddIgnoredPayee(row.PayeeName);

        // Remove all rows for this payee
        var toRemove = DetectedSubscriptions
            .Where(r => string.Equals(r.PayeeName, row.PayeeName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in toRemove)
            DetectedSubscriptions.Remove(item);

        DetectedCount = DetectedSubscriptions.Count;
    }

    /// <summary>
    /// Creates a subscription (bill) in Firefly III and a rule to auto-link
    /// matching transactions to it.
    /// </summary>
    [RelayCommand]
    private async Task CreateSubscriptionAsync(DetectedSubscriptionRow row)
    {
        if (row is null) return;

        var client = _fireflyService.Client;
        if (client is null)
        {
            ErrorMessage = "Not connected to Firefly III.";
            return;
        }

        row.IsCreating = true;
        ErrorMessage = string.Empty;

        try
        {
            // 1. Create the subscription/bill
            var firstDate = row.TransactionDates.FirstOrDefault();
            var toleranceFactor = AmountTolerancePercent / 100m;
            var amountMin = row.MinAmount * (1m - toleranceFactor);  // buffer below observed minimum
            var amountMax = row.MaxAmount * (1m + toleranceFactor);  // buffer above observed maximum
            var billStore = new BillStore
            {
                Name = row.PayeeName,
                Amount_min = amountMin.ToString("0.00", CultureInfo.InvariantCulture),
                Amount_max = amountMax.ToString("0.00", CultureInfo.InvariantCulture),
                Date = new DateTimeOffset(firstDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Repeat_freq = row.SuggestedFrequency,
                Active = true,                
                Notes = $"Auto-detected by FiiiAssist. {row.TransactionCount} occurrences found, avg interval {row.AverageIntervalDays:0} days.",
            };

            var billResult = await client.StoreBillAsync(null, billStore);
            var billId = billResult.Data.Id;
            var billName = billResult.Data.Attributes.Name;

            // 2. Ensure the rule group exists, create if not
            var ruleGroupId = await GetOrCreateRuleGroupAsync(client, "Subscriptions");

            // 3. Create a rule to auto-link future transactions to this subscription
            var ruleStore = new RuleStore
            {
                Title = $"Link to subscription: {row.PayeeName}",
                Description = $"Auto-created by FiiiAssist Subscription Detector. Links transactions from '{row.PayeeName}' to subscription '{billName}'.",
                Rule_group_id = ruleGroupId,
                Trigger = RuleTriggerType.StoreJournal,
                Active = true,                
                Strict = true,
                Stop_processing = false,
                Triggers =
                [
                    new RuleTriggerStore
                    {
                        Type = RuleTriggerKeyword.Description_contains,
                        Value = row.PayeeName,
                        Order = 1,
                        Active = true,
                        Stop_processing = false,
                    },
                    new RuleTriggerStore
                    {
                        Type = RuleTriggerKeyword.Amount_more,
                        Value = (row.MinAmount * (1m - toleranceFactor)).ToString("0.00", CultureInfo.InvariantCulture),
                        Order = 2,
                        Active = true,
                        Stop_processing = false,
                    },
                    new RuleTriggerStore
                    {
                        Type = RuleTriggerKeyword.Amount_less,
                        Value = (row.MaxAmount * (1m + toleranceFactor)).ToString("0.00", CultureInfo.InvariantCulture),
                        Order = 3,
                        Active = true,
                        Stop_processing = false,
                    },
                ],
                Actions =
                [
                    new RuleActionStore
                    {
                        Type = RuleActionKeyword.Link_to_bill,
                        Value = billName,
                        Order = 1,
                        Active = true,
                    },
                ],
            };

            await client.StoreRuleAsync(null, ruleStore);

            // Remove from the list on success
            DetectedSubscriptions.Remove(row);
            DetectedCount = DetectedSubscriptions.Count;
            StatusMessage = $"✔ Created subscription '{billName}' and matching rule.";
        }
        catch (ApiException<ValidationErrorResponse> apiEx)
        {
            ErrorMessage = $"API error creating subscription for '{row.PayeeName}': {apiEx.Result.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create subscription for '{row.PayeeName}': {ex.Message}";
        }
        finally
        {
            row.IsCreating = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds an existing rule group by title, or creates one if it doesn't exist.
    /// Returns the rule group ID.
    /// </summary>
    private static async Task<string> GetOrCreateRuleGroupAsync(Client client, string title)
    {
        // Search existing rule groups
        var page = 1;
        while (true)
        {
            var result = await client.ListRuleGroupAsync(null, 50, page);
            if (result?.Data == null || result.Data.Count == 0)
                break;

            var match = result.Data.FirstOrDefault(rg =>
                string.Equals(rg.Attributes?.Title, title, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match.Id;

            if (result.Data.Count < 50)
                break;

            page++;
        }

        // Not found — create it
        var newGroup = await client.StoreRuleGroupAsync(null, new RuleGroupStore
        {
            Title = title,
            Description = "Auto-created by FiiiAssist Subscription Detector.",
            Active = true,
        });

        return newGroup.Data.Id;
    }

    /// <summary>
    /// Groups transactions into clusters where amounts are within the given tolerance of each other.
    /// Uses a greedy approach: sort by amount, then merge adjacent items within tolerance.
    /// </summary>
    private static List<List<TransactionInfo>> FindAmountClusters(List<TransactionInfo> transactions, decimal tolerance)
    {
        var clusters = new List<List<TransactionInfo>>();
        var sorted = transactions.OrderBy(t => Math.Abs(t.Amount)).ToList();

        if (sorted.Count == 0)
            return clusters;

        var currentCluster = new List<TransactionInfo> { sorted[0] };
        var clusterBaseAmount = Math.Abs(sorted[0].Amount);

        for (int i = 1; i < sorted.Count; i++)
        {
            var amount = Math.Abs(sorted[i].Amount);

            // Check if within ±tolerance of the cluster's base amount
            if (clusterBaseAmount > 0 && Math.Abs(amount - clusterBaseAmount) / clusterBaseAmount <= tolerance)
            {
                currentCluster.Add(sorted[i]);
            }
            else
            {
                clusters.Add(currentCluster);
                currentCluster = [sorted[i]];
                clusterBaseAmount = amount;
            }
        }

        clusters.Add(currentCluster);
        return clusters;
    }

    /// <summary>
    /// Calculates the average interval in days between consecutive transaction dates.
    /// </summary>
    private static double CalculateAverageInterval(List<DateOnly> sortedDates)
    {
        if (sortedDates.Count < 2)
            return 0;

        var intervals = new List<int>();
        for (int i = 1; i < sortedDates.Count; i++)
        {
            intervals.Add(sortedDates[i].DayNumber - sortedDates[i - 1].DayNumber);
        }

        return intervals.Average();
    }

    /// <summary>
    /// Suggests a billing period label based on the average interval between transactions.
    /// </summary>
    private static string SuggestPeriod(double avgIntervalDays)
    {
        return avgIntervalDays switch
        {
            <= 0 => "Unknown",
            <= 10 => "Weekly",
            <= 21 => "Bi-weekly",
            <= 45 => "Monthly",
            <= 100 => "Quarterly",
            <= 200 => "Half-yearly",
            <= 400 => "Yearly",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Maps the average interval to a Firefly III BillRepeatFrequency enum value.
    /// </summary>
    private static BillRepeatFrequency SuggestFrequency(double avgIntervalDays)
    {
        return avgIntervalDays switch
        {
            <= 10 => BillRepeatFrequency.Weekly,
            <= 45 => BillRepeatFrequency.Monthly,
            <= 100 => BillRepeatFrequency.Quarterly,
            <= 200 => BillRepeatFrequency.HalfYear,
            _ => BillRepeatFrequency.Yearly,
        };
    }

    /// <summary>
    /// Fetches all transactions for the given account (full history).
    /// Returns lightweight info objects including the bill_id field.
    /// </summary>
    private async Task<List<TransactionInfo>> FetchTransactionsAsync(string accountId)
    {
        var results = new List<TransactionInfo>();
        var client = _fireflyService.Client;

        if (client is null)
            throw new InvalidOperationException("Not connected to Firefly III.");

        var page = 1;
        const int pageSize = 100;

        while (true)
        {
            var result = await client.ListTransactionByAccountAsync(
                x_Trace_Id: null,
                limit: pageSize,
                page: page,
                id: accountId,
                start: null,
                end: null,
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

                    // Determine the payee: for withdrawals it's the destination,
                    // for deposits it's the source
                    var payee = split.Type == TransactionTypeProperty.Withdrawal
                        ? split.Destination_name
                        : split.Source_name;

                    results.Add(new TransactionInfo
                    {
                        Date = DateOnly.FromDateTime(split.Date.DateTime),
                        Amount = amount,
                        PayeeName = payee ?? string.Empty,
                        BillId = split.Bill_id ?? split.Subscription_id ?? string.Empty,
                    });
                }
            }

            if (result.Data.Count < pageSize)
                break;

            page++;
        }

        return results;
    }

    /// <summary>
    /// Lightweight internal record for transaction analysis.
    /// </summary>
    private sealed class TransactionInfo
    {
        public DateOnly Date { get; set; }
        public decimal Amount { get; set; }
        public string PayeeName { get; set; } = string.Empty;
        public string BillId { get; set; } = string.Empty;
    }
}
