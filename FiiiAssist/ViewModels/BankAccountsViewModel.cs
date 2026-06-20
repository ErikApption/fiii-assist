using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiiiAssist.Models;
using FiiiAssist.Services;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace FiiiAssist.ViewModels;

public partial class BankAccountsViewModel : ObservableObject
{
    private readonly BankAccountMappingService _mappingService;
    private readonly FireflyIIIService _fireflyService;
    private bool _suppressSave;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasNoAccounts => Accounts.Count == 0 && !IsLoading;

    /// <summary>
    /// The list of bank accounts loaded from Firefly III, each with an editable regex pattern.
    /// </summary>
    public ObservableCollection<BankAccountRow> Accounts { get; } = [];

    public BankAccountsViewModel(
        BankAccountMappingService mappingService,
        FireflyIIIService fireflyService)
    {
        _mappingService = mappingService;
        _fireflyService = fireflyService;

        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoAccounts));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAccountsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var accounts = await _fireflyService.GetAccountsAsync();
            var savedMappings = _mappingService.Load();

            _suppressSave = true;
            Accounts.Clear();

            foreach (var account in accounts)
            {
                var id = account.Data?.Id ?? string.Empty;
                var name = account.Data?.Attributes?.Name ?? "(unnamed)";

                // Find any saved regex for this account
                var existing = savedMappings.FirstOrDefault(m => m.AccountId == id);

                var row = new BankAccountRow
                {
                    AccountId = id,
                    AccountName = name,
                    FileNamePattern = existing?.FileNamePattern ?? string.Empty,
                };
                row.PropertyChanged += (_, _) => SaveMappings();
                Accounts.Add(row);
            }

            _suppressSave = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load accounts: {ex.Message}";
        }
        finally
        {
            _suppressSave = false;
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoAccounts));
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void SaveMappings()
    {
        if (_suppressSave) return;

        // Only save accounts that have a pattern configured
        var list = Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.FileNamePattern))
            .Select(a => new BankAccountMapping
            {
                AccountId = a.AccountId,
                AccountName = a.AccountName,
                FileNamePattern = a.FileNamePattern,
            })
            .ToList();

        _mappingService.Save(list);
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the account ID whose filename pattern matches the given filename.
    /// Returns null if no match is found.
    /// </summary>
    public string? FindAccountForFile(string fileName)
    {
        var mappings = _mappingService.Load();

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.FileNamePattern) ||
                string.IsNullOrWhiteSpace(mapping.AccountId))
                continue;

            try
            {
                if (Regex.IsMatch(fileName, mapping.FileNamePattern, RegexOptions.IgnoreCase))
                    return mapping.AccountId;
            }
            catch (RegexParseException)
            {
                // Invalid regex — skip
            }
        }

        return null;
    }
}

/// <summary>
/// A single row in the bank accounts table: account info (read-only) + editable regex pattern.
/// </summary>
public partial class BankAccountRow : ObservableObject
{
    /// <summary>Firefly III account ID (read-only, from API).</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Account display name (read-only, from API).</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>User-editable regex pattern for matching QFX filenames.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternError))]
    [NotifyPropertyChangedFor(nameof(HasPatternError))]
    private string _fileNamePattern = string.Empty;

    /// <summary>
    /// Validates the regex pattern. Returns null if valid, error message if invalid.
    /// </summary>
    public string? PatternError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileNamePattern))
                return null;
            try
            {
                _ = new Regex(FileNamePattern);
                return null;
            }
            catch (RegexParseException ex)
            {
                return ex.Message;
            }
        }
    }

    public bool HasPatternError => PatternError != null;
}
