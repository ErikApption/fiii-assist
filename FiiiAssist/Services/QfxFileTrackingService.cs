using FiiiAssist.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FiiiAssist.Services;

/// <summary>
/// Scans the download folder for QFX files, extracts metadata (account ID, timestamp),
/// and persists their processing status so they are not re-prompted after being handled.
/// </summary>
public class QfxFileTrackingService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _trackingFilePath;
    private List<QfxFileEntry> _entries = [];

    public QfxFileTrackingService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiiiAssist");

        Directory.CreateDirectory(appDataFolder);
        _trackingFilePath = Path.Combine(appDataFolder, "qfx-file-tracking.json");
    }

    /// <summary>
    /// Scans the specified folder for QFX files and returns entries that are
    /// still pending (not yet imported or skipped).
    /// </summary>
    public IReadOnlyList<QfxFileEntry> GetPendingFiles(string watchFolder)
    {
        if (string.IsNullOrWhiteSpace(watchFolder) || !Directory.Exists(watchFolder))
            return [];

        LoadTracking();

        // Scan for all QFX files in the folder
        var qfxFiles = Directory.GetFiles(watchFolder, "*.qfx", SearchOption.TopDirectoryOnly);

        var pendingFiles = new List<QfxFileEntry>();

        foreach (var filePath in qfxFiles)
        {
            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Already tracked — include if still pending or previously failed
                if (existing.Status is QfxFileStatus.Pending or QfxFileStatus.Failed)
                    pendingFiles.Add(existing);
                continue;
            }

            // New file — extract metadata and add to tracking
            var entry = CreateEntry(filePath);
            _entries.Add(entry);
            pendingFiles.Add(entry);
        }

        SaveTracking();
        return pendingFiles;
    }

    /// <summary>
    /// Marks a file as imported.
    /// </summary>
    public void MarkImported(string filePath)
    {
        UpdateStatus(filePath, QfxFileStatus.Imported);
    }

    /// <summary>
    /// Marks a file as skipped (user chose not to import).
    /// </summary>
    public void MarkSkipped(string filePath)
    {
        UpdateStatus(filePath, QfxFileStatus.Skipped);
    }

    /// <summary>
    /// Marks a file as failed (import attempt failed).
    /// </summary>
    public void MarkFailed(string filePath)
    {
        UpdateStatus(filePath, QfxFileStatus.Failed);
    }

    /// <summary>
    /// Removes tracking entries for files that no longer exist on disk.
    /// </summary>
    public void CleanupStaleEntries()
    {
        LoadTracking();
        var before = _entries.Count;
        _entries.RemoveAll(e => !File.Exists(e.FilePath));
        if (_entries.Count != before)
            SaveTracking();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static QfxFileEntry CreateEntry(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        string accountId = string.Empty;

        try
        {
            accountId = QfxParserService.ExtractAccountId(filePath);
        }
        catch
        {
            // If we can't parse it, just leave account ID empty
        }

        return new QfxFileEntry
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            AccountId = accountId,
            FileTimestamp = fileInfo.LastWriteTimeUtc,
            Status = QfxFileStatus.Pending,
            DetectedAt = DateTime.UtcNow,
        };
    }

    private void UpdateStatus(string filePath, QfxFileStatus status)
    {
        LoadTracking();
        var entry = _entries.FirstOrDefault(e =>
            string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
        {
            entry.Status = status;
            SaveTracking();
        }
    }

    private void LoadTracking()
    {
        try
        {
            if (!File.Exists(_trackingFilePath))
            {
                _entries = [];
                return;
            }

            var json = File.ReadAllText(_trackingFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _entries = [];
                return;
            }

            _entries = JsonSerializer.Deserialize<List<QfxFileEntry>>(json, _jsonOptions) ?? [];
        }
        catch
        {
            _entries = [];
        }
    }

    private void SaveTracking()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, _jsonOptions);
            var tempPath = _trackingFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _trackingFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QfxTracking] Error saving: {ex.Message}");
        }
    }
}
