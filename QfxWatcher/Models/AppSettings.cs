namespace QfxWatcher.Models;

/// <summary>
/// Persisted application settings.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Base URL of the Actual Budget server, e.g. "http://localhost:5006".
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Password for the Actual Budget server.
    /// </summary>
    public string ServerPassword { get; set; } = string.Empty;

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
    /// Default Actual Budget account ID to import into (optional).
    /// </summary>
    public string DefaultAccountId { get; set; } = string.Empty;

    /// <summary>
    /// When true, TLS/SSL certificate validation is bypassed for server requests.
    /// </summary>
    public bool IgnoreSslCertificateValidation { get; set; }
}
