using QfxWatcher.Models;
using System.Text.Json;

namespace QfxWatcher.Services;

/// <summary>
/// Persists and loads bank account → filename regex mappings to a separate JSON file.
/// Stored alongside the main settings in the local app data folder.
/// </summary>
public class BankAccountMappingService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public BankAccountMappingService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QfxWatcher");

        Directory.CreateDirectory(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "bank-account-mappings.json");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public List<BankAccountMapping> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<BankAccountMapping>>(json, _jsonOptions)
                       ?? [];
            }
        }
        catch
        {
            // If the file is corrupt or unreadable, return empty list
        }

        return [];
    }

    public void Save(IReadOnlyList<BankAccountMapping> mappings)
    {
        try
        {
            var json = JsonSerializer.Serialize(mappings, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Swallow write errors to avoid crashing the app
        }
    }
}
