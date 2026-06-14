using QfxWatcher.Models;
using System.Text.Json;

namespace QfxWatcher.Services;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> to a JSON file in the local app data folder.
/// Works correctly for unpackaged WinUI 3 apps where ApplicationData.LocalSettings is unavailable.
/// Uses atomic writes (write-to-temp + rename) to prevent data loss if the app crashes mid-save.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;
    private readonly string _backupFilePath;

    public SettingsService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QfxWatcher");

        Directory.CreateDirectory(appDataFolder);
        _settingsFilePath = Path.Combine(appDataFolder, "settings.json");
        _backupFilePath = Path.Combine(appDataFolder, "settings.json.bak");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public AppSettings Load()
    {
        // Try primary file first
        var settings = TryLoadFrom(_settingsFilePath);
        if (settings is not null)
            return settings;

        // Primary is missing or corrupt — try backup
        settings = TryLoadFrom(_backupFilePath);
        if (settings is not null)
        {
            // Restore backup as primary so next save cycle works normally
            try { File.Copy(_backupFilePath, _settingsFilePath, overwrite: true); } catch { }
            return settings;
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);

            // Atomic write: write to a temp file, then replace the target.
            // This prevents corruption if the process is killed mid-write.
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Keep a backup of the previous good settings file
            if (File.Exists(_settingsFilePath))
            {
                File.Copy(_settingsFilePath, _backupFilePath, overwrite: true);
            }

            // Atomic rename (on NTFS this is atomic for same-volume moves)
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        catch
        {
            // Swallow write errors to avoid crashing the app
        }
    }

    private static AppSettings? TryLoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);

            // Guard against empty/whitespace-only files (truncated writes)
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
