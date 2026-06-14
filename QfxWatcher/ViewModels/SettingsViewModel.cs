using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
namespace QfxWatcher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService     _settings;
    private readonly FireflyIIIService _budget;
    private readonly FileWatcherService  _watcher;
    private bool _isLoading;
    private bool _hasInitialized;

    // Stores the last successfully loaded values so we can detect if a binding
    // initialization is attempting to blank out saved credentials.
    private string _loadedServerUrl = string.Empty;
    private string _loadedServerToken = string.Empty;

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
    private bool _skipDuplicateTransactions = true;

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
        // The _isLoading guard prevents save during Load(), and the
        // _hasInitialized guard prevents saves from XAML binding initialization
        // that can push empty values before the UI is fully ready.
        _hasInitialized = true;
        PropertyChanged += (_, e) =>
        {
            if (!_isLoading && _hasInitialized && IsPersistedProperty(e.PropertyName))
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
        nameof(SkipDuplicateTransactions) or
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
            SkipDuplicateTransactions = cfg.SkipDuplicateTransactions;
            LastConnectionSuccessful = cfg.LastConnectionSuccessful;
            DetectedFolder                 = FileWatcherService.DetectEdgeDownloadsFolder();

            _loadedServerUrl   = cfg.ServerUrl;
            _loadedServerToken = cfg.ServerToken;
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    public void Save()
    {
        // Guard: if ServerUrl or ServerToken have been blanked but were previously
        // loaded with values, this is likely a XAML binding initialization artifact.
        // Use the loaded values instead to prevent data loss.
        var urlToSave   = string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(_loadedServerUrl)
            ? _loadedServerUrl
            : ServerUrl.Trim();
        var tokenToSave = string.IsNullOrWhiteSpace(ServerToken) && !string.IsNullOrWhiteSpace(_loadedServerToken)
            ? _loadedServerToken
            : ServerToken;

        _settings.Save(new AppSettings
        {
            ServerUrl           = urlToSave,
            ServerToken         = tokenToSave,
            WatchFolder         = WatchFolder.Trim(),
            ArchiveAfterImport  = ArchiveAfterImport,
            ConfirmBeforeImport = ConfirmBeforeImport,
            DefaultAccountId              = DefaultAccountId,
            IgnoreSslCertificateValidation = IgnoreSslCertificateValidation,
            ErrorIfDuplicateHash = ErrorIfDuplicateHash,
            SkipDuplicateTransactions = SkipDuplicateTransactions,
            LastConnectionSuccessful = LastConnectionSuccessful,
        });

        // Update loaded values to reflect the save
        _loadedServerUrl   = urlToSave;
        _loadedServerToken = tokenToSave;
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
