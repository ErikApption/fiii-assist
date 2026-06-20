namespace FiiiAssist.Models;

/// <summary>
/// Tracks the status of a QFX file detected in the downloads folder.
/// </summary>
public class QfxFileEntry
{
    /// <summary>Full path to the QFX file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>File name only (for display).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Account ID extracted from the QFX file's ACCTID field.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>File's last-write timestamp (when the file was downloaded/created).</summary>
    public DateTime FileTimestamp { get; set; }

    /// <summary>Current processing status of this file.</summary>
    public QfxFileStatus Status { get; set; } = QfxFileStatus.Pending;

    /// <summary>When the file was first detected by the tracker.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Display-friendly timestamp string.</summary>
    public string TimestampText => FileTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>
/// Possible states for a tracked QFX file.
/// </summary>
public enum QfxFileStatus
{
    /// <summary>File detected but not yet acted upon.</summary>
    Pending,

    /// <summary>File was imported successfully.</summary>
    Imported,

    /// <summary>User chose to skip/dismiss this file.</summary>
    Skipped,

    /// <summary>Import was attempted but failed.</summary>
    Failed,
}
