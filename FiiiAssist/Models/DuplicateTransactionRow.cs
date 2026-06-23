using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FiiiAssist.Models;

/// <summary>
/// Represents a single transaction row displayed in the duplicate checker results.
/// Transactions with the same date and amount are flagged as potential duplicates.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public partial class DuplicateTransactionRow : ObservableObject
{
    /// <summary>Transaction date.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Transaction description / payee name.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Transaction amount (signed).</summary>
    public decimal Amount { get; set; }

    /// <summary>Display-friendly amount string.</summary>
    public string AmountText => Amount.ToString("0.00");

    /// <summary>Display-friendly date string.</summary>
    public string DateText => Date.ToString("yyyy-MM-dd");

    /// <summary>Source account name.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Destination account name.</summary>
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>Transaction type (Withdrawal, Deposit, Transfer).</summary>
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>Whether this row is flagged as a potential duplicate.</summary>
    public bool IsDuplicate { get; set; }

    /// <summary>External ID (FitId) if present.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Firefly III transaction ID for reference.</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Whether this row is selected for deletion.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether this row is the "kept" transaction in its duplicate group.
    /// Shown as a checkmark when another row in the same group is selected for deletion.
    /// </summary>
    [ObservableProperty]
    private bool _isKept;
}
