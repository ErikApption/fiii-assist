namespace FiiiAssist.Models;

/// <summary>
/// A subscription pattern that the user has chosen to ignore.
/// Persisted to JSON so it is excluded from future scans.
/// </summary>
public class IgnoredSubscriptionPattern
{
    /// <summary>Payee name (case-insensitive match).</summary>
    public string PayeeName { get; set; } = string.Empty;

    /// <summary>Approximate amount used to identify this pattern. Ignored when <see cref="IgnoreAllAmounts"/> is true.</summary>
    public decimal ApproximateAmount { get; set; }

    /// <summary>When true, all subscription patterns for this payee are ignored regardless of amount.</summary>
    public bool IgnoreAllAmounts { get; set; }

    /// <summary>When this pattern was ignored.</summary>
    public DateTime IgnoredAt { get; set; } = DateTime.UtcNow;
}
