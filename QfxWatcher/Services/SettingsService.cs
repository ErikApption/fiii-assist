using QfxWatcher.Models;
using System.Text.Json;
using Windows.Storage;

namespace QfxWatcher.Services;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> using <see cref="ApplicationData.LocalSettings"/>.
/// Falls back to an in-memory store when the packaged storage is unavailable (e.g. unit tests).
/// Also manages a JSON cache of <see cref="FireflyAccount"/> objects stored in the local app data folder.
/// </summary>
public class SettingsService
{
    private const string KeyServerUrl          = "ServerUrl";
    private const string KeyApiKey             = "ApiKey";
    private const string KeyWatchFolder        = "WatchFolder";
    private const string KeyArchiveAfterImport = "ArchiveAfterImport";
    private const string KeyConfirmBeforeImport = "ConfirmBeforeImport";
    private const string KeyDefaultAccountId    = "DefaultAccountId";
    private const string KeyIgnoreSslValidation = "IgnoreSslValidation";

    // In-memory fallback used when LocalSettings is not available.
    private readonly Dictionary<string, object?> _fallback = [];
    private ApplicationDataContainer? _container;

    // Path for the accounts JSON cache.
    private static readonly string AccountsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QfxWatcher",
        "accounts.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsService()
    {
        try { _container = ApplicationData.Current.LocalSettings; }
        catch { /* packaged storage not available – use fallback */ }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public AppSettings Load()
    {
        return new AppSettings
        {
            ServerUrl           = GetString(KeyServerUrl),
            ApiKey              = GetString(KeyApiKey),
            WatchFolder         = GetString(KeyWatchFolder),
            ArchiveAfterImport  = GetBool(KeyArchiveAfterImport, defaultValue: true),
            ConfirmBeforeImport         = GetBool(KeyConfirmBeforeImport, defaultValue: true),
            DefaultAccountId            = GetString(KeyDefaultAccountId),
            IgnoreSslCertificateValidation = GetBool(KeyIgnoreSslValidation),
        };
    }

    public void Save(AppSettings settings)
    {
        SetValue(KeyServerUrl,           settings.ServerUrl);
        SetValue(KeyApiKey,              settings.ApiKey);
        SetValue(KeyWatchFolder,         settings.WatchFolder);
        SetValue(KeyArchiveAfterImport,  settings.ArchiveAfterImport);
        SetValue(KeyConfirmBeforeImport, settings.ConfirmBeforeImport);
        SetValue(KeyDefaultAccountId,    settings.DefaultAccountId);
        SetValue(KeyIgnoreSslValidation, settings.IgnoreSslCertificateValidation);
    }

    // ── Accounts JSON cache ───────────────────────────────────────────────────

    /// <summary>
    /// Saves the given accounts list to the local JSON cache file.
    /// </summary>
    public void SaveAccounts(IEnumerable<FireflyAccount> accounts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AccountsCachePath)!);
            var json = JsonSerializer.Serialize(accounts.ToList(), JsonOptions);
            File.WriteAllText(AccountsCachePath, json);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Loads accounts from the local JSON cache file.
    /// Returns an empty list if the file does not exist or cannot be parsed.
    /// </summary>
    public IReadOnlyList<FireflyAccount> LoadAccounts()
    {
        try
        {
            if (!File.Exists(AccountsCachePath))
                return [];

            var json     = File.ReadAllText(AccountsCachePath);
            var accounts = JsonSerializer.Deserialize<List<FireflyAccount>>(json, JsonOptions);
            return accounts ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetString(string key, string defaultValue = "")
    {
        var raw = GetRaw(key);
        return raw is string s ? s : defaultValue;
    }

    private bool GetBool(string key, bool defaultValue = false)
    {
        var raw = GetRaw(key);
        return raw is bool b ? b : defaultValue;
    }

    private object? GetRaw(string key)
    {
        if (_container != null)
            return _container.Values.TryGetValue(key, out var v) ? v : null;
        return _fallback.TryGetValue(key, out var f) ? f : null;
    }

    private void SetValue(string key, object? value)
    {
        if (_container != null)
            _container.Values[key] = value;
        else
            _fallback[key] = value;
    }
}
