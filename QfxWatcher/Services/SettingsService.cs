using QfxWatcher.Models;
using Windows.Storage;

namespace QfxWatcher.Services;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> using <see cref="ApplicationData.LocalSettings"/>.
/// Falls back to an in-memory store when the packaged storage is unavailable (e.g. unit tests).
/// </summary>
public class SettingsService
{
    private const string KeyServerUrl          = "ServerUrl";
    private const string KeyServerPassword     = "ServerPassword";
    private const string KeyWatchFolder        = "WatchFolder";
    private const string KeyArchiveAfterImport = "ArchiveAfterImport";
    private const string KeyConfirmBeforeImport= "ConfirmBeforeImport";
    private const string KeyDefaultAccountId   = "DefaultAccountId";

    // In-memory fallback used when LocalSettings is not available.
    private readonly Dictionary<string, object?> _fallback = [];
    private ApplicationDataContainer? _container;

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
            ServerPassword      = GetString(KeyServerPassword),
            WatchFolder         = GetString(KeyWatchFolder),
            ArchiveAfterImport  = GetBool(KeyArchiveAfterImport, defaultValue: true),
            ConfirmBeforeImport = GetBool(KeyConfirmBeforeImport, defaultValue: true),
            DefaultAccountId    = GetString(KeyDefaultAccountId),
        };
    }

    public void Save(AppSettings settings)
    {
        SetValue(KeyServerUrl,           settings.ServerUrl);
        SetValue(KeyServerPassword,      settings.ServerPassword);
        SetValue(KeyWatchFolder,         settings.WatchFolder);
        SetValue(KeyArchiveAfterImport,  settings.ArchiveAfterImport);
        SetValue(KeyConfirmBeforeImport, settings.ConfirmBeforeImport);
        SetValue(KeyDefaultAccountId,    settings.DefaultAccountId);
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
