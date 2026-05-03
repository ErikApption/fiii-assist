using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace QfxWatcher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService    _settings;
    private readonly FireflyIIIService  _firefly;
    private readonly FileWatcherService _watcher;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _watchFolder = string.Empty;

    [ObservableProperty]
    private bool _archiveAfterImport = true;

    [ObservableProperty]
    private bool _confirmBeforeImport = true;

    [ObservableProperty]
    private string _defaultAccountId = string.Empty;

    [ObservableProperty]
    private bool _ignoreSslCertificateValidation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestConnectionMessage))]
    private string _testConnectionMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestConnection))]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _detectedFolder = string.Empty;

    public bool CanTestConnection => !IsTestingConnection;

    public bool HasTestConnectionMessage => !string.IsNullOrWhiteSpace(TestConnectionMessage);

    public bool HasAccounts => Accounts.Count > 0;

    public ObservableCollection<FireflyAccount> Accounts { get; } = [];

    public SettingsViewModel(
        SettingsService    settings,
        FireflyIIIService  firefly,
        FileWatcherService watcher)
    {
        _settings = settings;
        _firefly  = firefly;
        _watcher  = watcher;

        Accounts.CollectionChanged += OnAccountsCollectionChanged;

        Load();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public void Load()
    {
        var cfg = _settings.Load();
        ServerUrl           = cfg.ServerUrl;
        ApiKey              = cfg.ApiKey;
        WatchFolder         = cfg.WatchFolder;
        ArchiveAfterImport  = cfg.ArchiveAfterImport;
        ConfirmBeforeImport = cfg.ConfirmBeforeImport;
        DefaultAccountId              = cfg.DefaultAccountId;
        IgnoreSslCertificateValidation = cfg.IgnoreSslCertificateValidation;
        DetectedFolder                 = FileWatcherService.DetectEdgeDownloadsFolder();

        // Pre-populate accounts from the JSON cache
        var cached = _settings.LoadAccounts();
        Accounts.Clear();
        foreach (var a in cached)
            Accounts.Add(a);
    }

    [RelayCommand]
    public void Save()
    {
        _settings.Save(new AppSettings
        {
            ServerUrl           = ServerUrl.Trim(),
            ApiKey              = ApiKey,
            WatchFolder         = WatchFolder.Trim(),
            ArchiveAfterImport  = ArchiveAfterImport,
            ConfirmBeforeImport = ConfirmBeforeImport,
            DefaultAccountId              = DefaultAccountId,
            IgnoreSslCertificateValidation = IgnoreSslCertificateValidation,
        });
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            TestConnectionMessage = "Please enter a server URL first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            TestConnectionMessage = "Please enter an API key first.";
            return;
        }

        IsTestingConnection   = true;
        TestConnectionMessage = "Connecting…";
        Accounts.Clear();

        try
        {
            _firefly.Configure(ServerUrl.Trim(), ApiKey, IgnoreSslCertificateValidation);
            var accounts = await _firefly.GetAccountsAsync();

            foreach (var a in accounts)
                Accounts.Add(a);

            // Persist the fetched accounts to the JSON cache
            _settings.SaveAccounts(accounts);

            TestConnectionMessage = accounts.Count > 0
                ? $"✔ Connected. Found {accounts.Count} account(s)."
                : "✔ Connected, but no accounts found.";
        }
        catch (Exception ex)
        {
            TestConnectionMessage = $"❌ Connection error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAccounts));
    }

    [RelayCommand]
    public void UseDetectedFolder()
    {
        WatchFolder = DetectedFolder;
    }

    [RelayCommand]
    public void ClearWatchFolder()
    {
        WatchFolder = string.Empty;
    }
}
