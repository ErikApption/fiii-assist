using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Collections.ObjectModel;

namespace QfxWatcher.ViewModels;

/// <summary>
/// ViewModel for the Dashboard page.  Owns the FileWatcher and coordinates
/// detecting QFX files with calling the Firefly III service.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly SettingsService      _settings;
    private readonly FileWatcherService   _watcher;
    private readonly FireflyIIIService  _budget;

    [ObservableProperty]
    private string _statusMessage = "Not started.";

    [ObservableProperty]
    private string _watchedFolder = string.Empty;

    [ObservableProperty]
    private bool _isWatching;

    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Log entries shown in the dashboard list.</summary>
    public ObservableCollection<ImportLogEntry> LogEntries { get; } = [];

    /// <summary>
    /// The Import Wizard ViewModel exposed for data-binding by the DashboardPage.
    /// </summary>
    public ImportWizardViewModel ImportWizard { get; }

    /// <summary>
    /// Raised when a QFX file is detected and the ViewModel needs the
    /// View to show the import confirmation dialog.
    /// The string argument is the full file path.
    /// </summary>
    public event EventHandler<string>? ImportRequested;

    public DashboardViewModel(
        SettingsService     settings,
        FileWatcherService  watcher,
        FireflyIIIService budget,
        ImportWizardViewModel importWizard)
    {
        _settings = settings;
        _watcher  = watcher;
        _budget   = budget;
        ImportWizard = importWizard;

        _watcher.QfxFileDetected += OnQfxFileDetected;
        ImportWizard.ImportCompleted += OnImportWizardCompleted;

        // Auto-start the file watcher if the last connection test was successful.
        // The actual Firefly III connection is established by SettingsViewModel's
        // auto-connect on launch — we just start watching the folder here.
        var cfg = _settings.Load();
        if (cfg.LastConnectionSuccessful &&
            !string.IsNullOrWhiteSpace(cfg.ServerUrl) &&
            !string.IsNullOrWhiteSpace(cfg.ServerToken))
        {
            AutoStartWatcher(cfg);
        }
    }

    /// <summary>
    /// Starts the file watcher without attempting a Firefly III connection.
    /// The connection is handled by SettingsViewModel's auto-connect on launch.
    /// </summary>
    private void AutoStartWatcher(AppSettings cfg)
    {
        var folder = string.IsNullOrWhiteSpace(cfg.WatchFolder)
            ? FileWatcherService.DetectEdgeDownloadsFolder()
            : cfg.WatchFolder;

        try
        {
            _watcher.Start(folder);
            WatchedFolder = folder;
            IsWatching = true;
            StatusMessage = $"Watching: {folder} (connecting…)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting watcher: {ex.Message}";
        }
    }

    /// <summary>
    /// Called after the SettingsViewModel successfully connects to Firefly III.
    /// Updates the dashboard connection state.
    /// </summary>
    public void NotifyConnected()
    {
        IsConnected = true;
        if (IsWatching)
            StatusMessage = $"Watching: {WatchedFolder} | Connected to Firefly III";
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task StartWatchingAsync()
    {
        var cfg = _settings.Load();

        // Resolve the watched folder
        var folder = string.IsNullOrWhiteSpace(cfg.WatchFolder)
            ? FileWatcherService.DetectEdgeDownloadsFolder()
            : cfg.WatchFolder;

        try
        {
            _watcher.Start(folder);
            WatchedFolder  = folder;
            IsWatching     = true;
            StatusMessage  = $"Watching: {folder}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting watcher: {ex.Message}";
        }

        // Try to connect to Firefly III
        await TryConnectAsync(cfg);
    }

    [RelayCommand]
    public void StopWatching()
    {
        _watcher.Stop();
        IsWatching    = false;
        StatusMessage = "Watcher stopped.";
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task TryConnectAsync(AppSettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ServerUrl) ||
            string.IsNullOrWhiteSpace(cfg.ServerToken))
        {
            IsConnected   = false;
            StatusMessage = IsWatching
                ? $"Watching: {WatchedFolder} (Firefly III not configured)"
                : "Firefly III not configured.";
            return;
        }

        try
        {
            _budget.Configure(cfg.ServerUrl, cfg.IgnoreSslCertificateValidation);
            IsConnected  = await _budget.LoginAsync(cfg.ServerToken);
            StatusMessage = IsConnected
                ? $"Watching: {WatchedFolder} | Connected to Firefly III"
                : $"Watching: {WatchedFolder} | Failed to connect to Firefly III";
        }
        catch (Exception ex)
        {
            IsConnected   = false;
            StatusMessage = $"Watching: {WatchedFolder} | Connection error: {ex.Message}";
        }
    }

    private void OnQfxFileDetected(object? sender, string filePath)
    {
        // Raise on UI thread via dispatcher – the View handles the dialog
        ImportRequested?.Invoke(this, filePath);
    }

    private void OnImportWizardCompleted(object? sender, ImportLogEntry entry)
    {
        LogEntries.Insert(0, entry);
    }

    /// <summary>
    /// Called by the View after the user confirms an import.
    /// </summary>
    public async Task ExecuteImportAsync(string filePath, string accountId, string accountName)
    {
        try
        {
            var transactions = QfxParserService.ParseFile(filePath);

            var cfg      = _settings.Load();
            var targetId = string.IsNullOrWhiteSpace(accountId)
                ? cfg.DefaultAccountId
                : accountId;

            int added = 0;
            int skipped = 0;
            if (!string.IsNullOrWhiteSpace(targetId) && IsConnected)
            {
                // Skip duplicates if enabled
                IReadOnlyList<FIIITransaction> toImport = transactions;
                if (cfg.SkipDuplicateTransactions && transactions.Count > 0)
                {
                    try
                    {
                        var dates = transactions.Select(t => t.Date).ToList();
                        var existingIds = await _budget.GetExistingExternalIdsAsync(
                            targetId, dates.Min(), dates.Max());

                        var filtered = transactions
                            .Where(t => string.IsNullOrWhiteSpace(t.FitId) || !existingIds.Contains(t.FitId))
                            .ToList();
                        skipped = transactions.Count - filtered.Count;
                        toImport = filtered;
                    }
                    catch
                    {
                        // If lookup fails, import all — server-side dedup still applies
                    }
                }

                added = await _budget.ImportTransactionsAsync(targetId, toImport, cfg.ErrorIfDuplicateHash);
            }

            var entry = new ImportLogEntry
            {
                FileName         = Path.GetFileName(filePath),
                AccountName      = accountName,
                TransactionCount = added > 0 ? added : transactions.Count,
                SkippedCount     = skipped,
                Success          = true,
            };
            LogEntries.Insert(0, entry);

            // Archive the file if configured
            if (cfg.ArchiveAfterImport)
                ArchiveFile(filePath);
        }
        catch (Exception ex)
        {
            LogEntries.Insert(0, new ImportLogEntry
            {
                FileName     = Path.GetFileName(filePath),
                AccountName  = accountName,
                Success      = false,
                ErrorMessage = ex.Message,
            });
        }
    }

    private static void ArchiveFile(string filePath)
    {
        try
        {
            var dir     = Path.GetDirectoryName(filePath)!;
            var archive = Path.Combine(dir, "imported");
            Directory.CreateDirectory(archive);
            var dest = Path.Combine(archive, Path.GetFileName(filePath));
            if (File.Exists(dest))
                dest = Path.Combine(archive,
                    $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}.qfx");
            File.Move(filePath, dest);
        }
        catch { /* best-effort */ }
    }
}
