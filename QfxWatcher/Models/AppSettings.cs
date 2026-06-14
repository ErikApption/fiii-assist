namespace QfxWatcher.Models;

/// <summary>
/// Persisted application settings.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Base URL of the Firefly III server, e.g. "https://firefly.example.com".
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Personal Access Token for the Firefly III API.
    /// </summary>
    public string ServerToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional: override the watched folder path.
    /// When empty the app auto-detects the Edge downloads folder.
    /// </summary>
    public string WatchFolder { get; set; } = string.Empty;

    /// <summary>
    /// When true, imported QFX files are moved to a sub-folder after upload.
    /// </summary>
    public bool ArchiveAfterImport { get; set; } = true;

    /// <summary>
    /// When true, show a confirmation dialog before uploading each file.
    /// When false, uploads happen automatically.
    /// </summary>
    public bool ConfirmBeforeImport { get; set; } = true;

    /// <summary>
    /// Default Firefly III asset account ID to import into (optional).
    /// </summary>
    public string DefaultAccountId { get; set; } = string.Empty;

    /// <summary>
    /// When true, TLS/SSL certificate validation is bypassed for server requests.
    /// </summary>
    public bool IgnoreSslCertificateValidation { get; set; }

    /// <summary>
    /// When true, Firefly III will treat transactions with duplicate hashes as errors.
    /// When false, duplicate transactions are silently ignored.
    /// </summary>
    public bool ErrorIfDuplicateHash { get; set; }

    /// <summary>
    /// When true, transactions whose FitId (external_id) already exists in Firefly III
    /// for the target account are skipped during import to prevent duplicates.
    /// </summary>
    public bool SkipDuplicateTransactions { get; set; } = true;

    /// <summary>
    /// Persisted result of the last successful connection test.
    /// When true, the app will auto-connect on startup using the saved credentials.
    /// </summary>
    public bool LastConnectionSuccessful { get; set; }
}
