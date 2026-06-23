using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiiiAssist.FireflyIII;
using FiiiAssist.Models;
using FiiiAssist.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
namespace FiiiAssist.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService     _settings;
    private readonly FireflyIIIService _budget;
    private readonly FileWatcherService  _watcher;
    private bool _isLoading;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _serverToken = string.Empty;

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
    private bool _errorIfDuplicateHash;

    [ObservableProperty]
    private bool _useBatchMode = true;

    [ObservableProperty]
    private bool _skipDuplicateTransactions = true;

    [ObservableProperty]
    private bool _skipDuplicatesByContent;

    [ObservableProperty]
    private bool _lastConnectionSuccessful;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestConnectionMessage))]
    private string _testConnectionMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestConnection))]
    private bool _isTestingConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConnected))]
    private bool _isConnected;

    [ObservableProperty]
    private string _detectedFolder = string.Empty;

    public bool CanTestConnection => !IsTestingConnection;

    public bool HasTestConnectionMessage => !string.IsNullOrWhiteSpace(TestConnectionMessage);

    public bool HasAccounts => Accounts.Count > 0;

    public bool IsNotConnected => !IsConnected;

    public ObservableCollection<AccountSingle> Accounts { get; } = [];
    public SettingsViewModel(
        SettingsService     settings,
        FireflyIIIService budget,
        FileWatcherService  watcher)
    {
        _settings = settings;
        _budget   = budget;
        _watcher  = watcher;

        Accounts.CollectionChanged += OnAccountsCollectionChanged;

        Load();

        // Auto-persist whenever a user-editable property changes.
        // The _isLoading guard prevents save during Load().
        PropertyChanged += (_, e) =>
        {
            if (!_isLoading && IsPersistedProperty(e.PropertyName))
                Save();
        };
    }

    private static bool IsPersistedProperty(string? name) => name is
        nameof(ServerUrl) or
        nameof(ServerToken) or
        nameof(WatchFolder) or
        nameof(ArchiveAfterImport) or
        nameof(ConfirmBeforeImport) or
        nameof(DefaultAccountId) or
        nameof(IgnoreSslCertificateValidation) or
        nameof(ErrorIfDuplicateHash) or
        nameof(UseBatchMode) or
        nameof(SkipDuplicateTransactions) or
        nameof(SkipDuplicatesByContent) or
        nameof(LastConnectionSuccessful);

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public void Load()
    {
        _isLoading = true;
        try
        {
            var cfg = _settings.Load();
            ServerUrl           = cfg.ServerUrl;
            ServerToken         = cfg.ServerToken;
            WatchFolder         = cfg.WatchFolder;
            ArchiveAfterImport  = cfg.ArchiveAfterImport;
            ConfirmBeforeImport = cfg.ConfirmBeforeImport;
            DefaultAccountId              = cfg.DefaultAccountId;
            IgnoreSslCertificateValidation = cfg.IgnoreSslCertificateValidation;
            ErrorIfDuplicateHash = cfg.ErrorIfDuplicateHash;
            UseBatchMode = cfg.UseBatchMode;
            SkipDuplicateTransactions = cfg.SkipDuplicateTransactions;
            SkipDuplicatesByContent = cfg.SkipDuplicatesByContent;
            LastConnectionSuccessful = cfg.LastConnectionSuccessful;
            DetectedFolder                 = FileWatcherService.DetectEdgeDownloadsFolder();
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    public void Save()
    {
        _settings.Save(new AppSettings
        {
            ServerUrl           = ServerUrl.Trim(),
            ServerToken         = ServerToken,
            WatchFolder         = WatchFolder.Trim(),
            ArchiveAfterImport  = ArchiveAfterImport,
            ConfirmBeforeImport = ConfirmBeforeImport,
            DefaultAccountId              = DefaultAccountId,
            IgnoreSslCertificateValidation = IgnoreSslCertificateValidation,
            ErrorIfDuplicateHash = ErrorIfDuplicateHash,
            UseBatchMode = UseBatchMode,
            SkipDuplicateTransactions = SkipDuplicateTransactions,
            SkipDuplicatesByContent = SkipDuplicatesByContent,
            LastConnectionSuccessful = LastConnectionSuccessful,
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

        if (string.IsNullOrWhiteSpace(ServerToken))
        {
            TestConnectionMessage = "Please enter a Personal Access Token first.";
            return;
        }

        IsTestingConnection   = true;
        IsConnected           = false;
        TestConnectionMessage = "Connecting…";
        Accounts.Clear();

        try
        {
            _budget.Configure(ServerUrl.Trim(), IgnoreSslCertificateValidation);
            var ok = await _budget.LoginAsync(ServerToken);

            if (!ok)
            {
                TestConnectionMessage = "❌ Authentication failed. Check your token.";
                LastConnectionSuccessful = false;
                return;
            }

            var accounts = await _budget.GetAccountsAsync();
            foreach (var a in accounts)
                Accounts.Add(a);

            IsConnected = true;
            LastConnectionSuccessful = true;
            TestConnectionMessage = accounts.Count > 0
                ? $"✔ Connected. Found {accounts.Count} account(s)."
                : "✔ Connected, but no accounts found.";
        }
        catch (Exception ex)
        {
            LastConnectionSuccessful = false;
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
