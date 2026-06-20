namespace FiiiAssist.Services.Csv.Conversion;

/// <summary>
/// Configuration for a CSV import job, defining column roles, mappings, and behavior.
/// </summary>
public sealed class CsvImportConfiguration
{
    /// <summary>
    /// Maps column index to its role (e.g. "description", "amount", "date_transaction").
    /// </summary>
    public Dictionary<int, string> Roles { get; set; } = [];

    /// <summary>
    /// Maps column index → (value → mapped ID). Used for mapping CSV values to Firefly III entity IDs.
    /// </summary>
    public Dictionary<int, Dictionary<string, int>> Mapping { get; set; } = [];

    /// <summary>
    /// Which columns have mapping enabled.
    /// </summary>
    public HashSet<int> DoMapping { get; set; } = [];

    /// <summary>
    /// Date format string (PHP-style, e.g. "d/m/Y" or "en:Y-m-d").
    /// </summary>
    public string DateFormat { get; set; } = "Y-m-d";

    /// <summary>
    /// Default asset account ID to use when no source account is found.
    /// </summary>
    public string? DefaultAccountId { get; set; }

    /// <summary>
    /// CSV delimiter character.
    /// </summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>
    /// Whether the CSV file has a header row.
    /// </summary>
    public bool HasHeaders { get; set; } = true;

    /// <summary>
    /// Whether to skip duplicate lines in the CSV.
    /// </summary>
    public bool IgnoreDuplicateLines { get; set; } = true;

    /// <summary>
    /// Whether to set error_if_duplicate_hash on the Firefly III transaction.
    /// </summary>
    public bool IgnoreDuplicateTransactions { get; set; } = true;

    /// <summary>
    /// Whether to apply Firefly III rules to imported transactions.
    /// </summary>
    public bool ApplyRules { get; set; } = true;

    /// <summary>
    /// Whether to fire webhooks for imported transactions.
    /// </summary>
    public bool FireWebhooks { get; set; } = true;
}
