namespace QfxWatcher.Models;

/// <summary>
/// Represents one QFX import event shown in the dashboard log.
/// </summary>
public class ImportLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string FileName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public int SkippedCount { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public string StatusText => Success
        ? SkippedCount > 0
            ? $"✔ {TransactionCount} transaction(s) imported into '{AccountName}' ({SkippedCount} duplicate(s) skipped)"
            : $"✔ {TransactionCount} transaction(s) imported into '{AccountName}'"
        : $"✘ Failed – {ErrorMessage}";

    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
}
