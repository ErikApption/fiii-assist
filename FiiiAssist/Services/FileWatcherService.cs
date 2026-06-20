using Microsoft.Win32;

namespace FiiiAssist.Services;

/// <summary>
/// Detects and monitors a folder for newly-created QFX files.
/// Raises <see cref="QfxFileDetected"/> on the thread pool; callers
/// must dispatch to the UI thread themselves.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string _watchedFolder = string.Empty;

    /// <summary>Raised when a new QFX file appears in the watched folder.</summary>
    public event EventHandler<string>? QfxFileDetected;

    /// <summary>The folder currently being watched.</summary>
    public string WatchedFolder => _watchedFolder;

    // ── Auto-detection ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to find the Microsoft Edge downloads folder.
    /// Priority:
    ///   1. Edge profile preferences file (per-user)
    ///   2. Default Windows Downloads folder (%USERPROFILE%\Downloads)
    /// </summary>
    public static string DetectEdgeDownloadsFolder()
    {
        // 1. Try reading Edge's preferences JSON
        try
        {
            var edgePrefDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data");

            if (Directory.Exists(edgePrefDir))
            {
                // Check all profile folders (Default, Profile 1, Profile 2, …)
                var profileDirs = Directory.EnumerateDirectories(edgePrefDir)
                    .Where(d => Path.GetFileName(d).StartsWith("Default", StringComparison.OrdinalIgnoreCase)
                             || Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));

                foreach (var profile in profileDirs)
                {
                    var prefFile = Path.Combine(profile, "Preferences");
                    if (!File.Exists(prefFile)) continue;

                    var json = File.ReadAllText(prefFile);
                    var folder = ExtractDownloadDirFromJson(json);
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        return folder;
                }
            }
        }
        catch (Exception)
        {
            // Fall through to default
        }

        // 2. Fall back to the Windows "Downloads" shell folder
        return GetDefaultDownloadsFolder();
    }

    /// <summary>Returns the current user's Downloads folder path.</summary>
    public static string GetDefaultDownloadsFolder()
    {
        // Try shell folder registry key first (most reliable)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
            var val = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                return val;
        }
        catch { /* ignore */ }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    // ── Watcher lifecycle ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts watching <paramref name="folder"/> for new QFX files.
    /// Stops any previously active watcher.
    /// </summary>
    public void Start(string folder)
    {
        Stop();

        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Watched folder not found: {folder}");

        _watchedFolder = folder;

        _watcher = new FileSystemWatcher(folder)
        {
            Filter                = "*.qfx",
            NotifyFilter          = NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents   = true,
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
    }

    /// <summary>Stops watching the folder.</summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created            -= OnFileCreated;
            _watcher.Renamed            -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose() => Stop();

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (IsQfxFile(e.FullPath))
            OnQfxDetected(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Browsers often write to a temp file first then rename to the real name
        if (IsQfxFile(e.FullPath))
            OnQfxDetected(e.FullPath);
    }

    private void OnQfxDetected(string path)
    {
        // Wait briefly to let the browser finish writing before we try to read
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            QfxFileDetected?.Invoke(this, path);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsQfxFile(string path) =>
        string.Equals(Path.GetExtension(path), ".qfx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Minimal JSON extraction for the "savefile" > "default_directory" path
    /// in Edge's Preferences file without pulling in an extra JSON library.
    /// </summary>
    private static string ExtractDownloadDirFromJson(string json)
    {
        // Look for "default_directory": "C:\\path\\to\\folder"
        const string marker = "\"default_directory\"";
        var idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var colon = json.IndexOf(':', idx + marker.Length);
        if (colon < 0) return string.Empty;

        var quote1 = json.IndexOf('"', colon + 1);
        if (quote1 < 0) return string.Empty;

        var quote2 = quote1 + 1;
        while (quote2 < json.Length)
        {
            if (json[quote2] == '"' && (quote2 == 0 || json[quote2 - 1] != '\\')) break;
            quote2++;
        }

        if (quote2 >= json.Length) return string.Empty;

        // Unescape JSON string (handle \\ and \/)
        return json[(quote1 + 1)..quote2]
            .Replace("\\\\", "\\")
            .Replace("\\/", "/");
    }
}
