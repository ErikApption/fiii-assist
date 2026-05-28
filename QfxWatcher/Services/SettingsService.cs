using QfxWatcher.Models;
using System.Text.Json;

namespace QfxWatcher.Services;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> to a JSON file in the local app data folder.
/// Works correctly for unpackaged WinUI 3 apps where ApplicationData.LocalSettings is unavailable.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QfxWatcher");

        Directory.CreateDirectory(appDataFolder);
        _settingsFilePath = Path.Combine(appDataFolder, "settings.json");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // If the file is corrupt or unreadable, return defaults
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Swallow write errors to avoid crashing the app
        }
    }
}
