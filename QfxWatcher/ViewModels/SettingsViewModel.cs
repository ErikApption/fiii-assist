using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace QfxWatcher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService     _settings;
    private readonly ActualBudgetService _budget;
    private readonly FileWatcherService  _watcher;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _serverPassword = string.Empty;

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

    public ObservableCollection<ActualAccount> Accounts { get; } = [];

    public SettingsViewModel(
        SettingsService     settings,
        ActualBudgetService budget,
        FileWatcherService  watcher)
    {
        _settings = settings;
        _budget   = budget;
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
        ServerPassword      = cfg.ServerPassword;
        WatchFolder         = cfg.WatchFolder;
        ArchiveAfterImport  = cfg.ArchiveAfterImport;
        ConfirmBeforeImport = cfg.ConfirmBeforeImport;
        DefaultAccountId              = cfg.DefaultAccountId;
        IgnoreSslCertificateValidation = cfg.IgnoreSslCertificateValidation;
        DetectedFolder                 = FileWatcherService.DetectEdgeDownloadsFolder();
    }

    [RelayCommand]
    public void Save()
    {
        _settings.Save(new AppSettings
        {
            ServerUrl           = ServerUrl.Trim(),
            ServerPassword      = ServerPassword,
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

        IsTestingConnection   = true;
        TestConnectionMessage = "Connecting…";
        Accounts.Clear();

        try
        {
            _budget.Configure(ServerUrl.Trim(), IgnoreSslCertificateValidation);
            var ok = await _budget.LoginAsync(ServerPassword);

            if (!ok)
            {
                TestConnectionMessage = "❌ Authentication failed. Check your password.";
                return;
            }

            var accounts = await _budget.GetAccountsAsync();
            foreach (var a in accounts)
                Accounts.Add(a);

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
