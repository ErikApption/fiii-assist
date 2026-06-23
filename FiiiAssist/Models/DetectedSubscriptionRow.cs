using CommunityToolkit.Mvvm.ComponentModel;
using FiiiAssist.FireflyIII;
using System;
using System.Collections.Generic;

namespace FiiiAssist.Models;

/// <summary>
/// Represents a detected subscription pattern: a payee with repeated similar amounts.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public partial class DetectedSubscriptionRow : ObservableObject
{
    /// <summary>Payee / merchant name (destination for withdrawals, source for deposits).</summary>
    public string PayeeName { get; set; } = string.Empty;

    /// <summary>Average amount of the recurring transactions.</summary>
    public decimal AverageAmount { get; set; }

    /// <summary>Minimum amount seen.</summary>
    public decimal MinAmount { get; set; }

    /// <summary>Maximum amount seen.</summary>
    public decimal MaxAmount { get; set; }

    /// <summary>Number of matching transactions found.</summary>
    public int TransactionCount { get; set; }

    /// <summary>Suggested repeat frequency label (e.g. "Monthly", "Weekly").</summary>
    public string SuggestedPeriod { get; set; } = string.Empty;

    /// <summary>Suggested Firefly III repeat frequency enum value.</summary>
    public BillRepeatFrequency SuggestedFrequency { get; set; }

    /// <summary>Average interval between transactions in days.</summary>
    public double AverageIntervalDays { get; set; }

    /// <summary>The individual transaction dates, sorted ascending.</summary>
    public List<DateOnly> TransactionDates { get; set; } = [];

    /// <summary>Display-friendly average amount.</summary>
    public string AverageAmountText => AverageAmount.ToString("0.00");

    /// <summary>Display-friendly amount range.</summary>
    public string AmountRangeText => MinAmount == MaxAmount
        ? MinAmount.ToString("0.00")
        : $"{MinAmount:0.00} – {MaxAmount:0.00}";

    /// <summary>Whether the user has selected this row (for potential bill creation).</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether a create subscription operation is in progress for this row.</summary>
    [ObservableProperty]
    private bool _isCreating;
}
