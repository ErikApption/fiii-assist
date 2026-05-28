using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace QfxWatcher.Services;

/// <summary>
/// Service adapter that maps the app's backend operations to Firefly III.
/// All API calls are delegated to the generated <see cref="Client"/>.
/// </summary>
public sealed class FireflyIIIService : IDisposable
{
    private HttpClient? _ownedHttpClient;
    private Client? _client;

    /// <summary>
    /// Creates an unconfigured instance. Call <see cref="Configure"/> before use.
    /// </summary>
    public FireflyIIIService() { }

    /// <summary>
    /// Creates an instance backed by a pre-configured <see cref="Client"/>.
    /// The caller owns the HttpClient lifetime.
    /// </summary>
    public FireflyIIIService(Client client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Configures the service by creating a <see cref="Client"/> pointed at the given server URL.
    /// </summary>
    public void Configure(string serverUrl, bool ignoreSslCertificateValidation = false)
    {
        DisposeOwned();

        var baseUrl = serverUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return;

        var handler = new HttpClientHandler();
        if (ignoreSslCertificateValidation)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _ownedHttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute)
        };

        _client = new Client(_ownedHttpClient)
        {
            BaseUrl = baseUrl.TrimEnd('/') + "/api/"
        };
    }

    /// <summary>
    /// Authenticates with the Firefly III server using a Personal Access Token.
    /// Returns true if the token is valid.
    /// </summary>
    public async Task<bool> LoginAsync(string password)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(password))
            return false;

        _ownedHttpClient!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", password.Trim());

        try
        {
            _ = await _client!.ListAccountAsync(
                x_Trace_Id: null,
                limit: 1,
                page: 1,
                start: null,
                end: null,
                date: null,
                type: AccountTypeFilter.Asset);

            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            _ownedHttpClient.DefaultRequestHeaders.Authorization = null;
            return false;
        }
    }

    /// <summary>
    /// Tests whether the configured client can reach the Firefly III API.
    /// Use this when the Client was injected with auth already configured.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        EnsureConfigured();

        try
        {
            _ = await _client!.ListAccountAsync(
                x_Trace_Id: null,
                limit: 1,
                page: 1,
                start: null,
                end: null,
                date: null,
                type: AccountTypeFilter.Asset);

            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<AccountSingle>> GetAccountsAsync()
    {
        EnsureConfigured();

        var response = await _client!.ListAccountAsync(
            x_Trace_Id: null,
            limit: 200,
            page: 1,
            start: null,
            end: null,
            date: null,
            type: AccountTypeFilter.Asset);

        return response.Data
            .Select(a => new AccountSingle
            {
                Data = a
            })
            .ToList();
    }

    public async Task<int> ImportTransactionsAsync(string accountId, IReadOnlyList<FIIITransaction> transactions, bool errorIfDuplicateHash = false)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(accountId) || transactions.Count == 0)
            return 0;

        // Pre-fetch accounts so we can match transfer targets by name
        IReadOnlyList<AccountSingle> accounts;
        try
        {
            accounts = await GetAccountsAsync();
        }
        catch
        {
            accounts = [];
        }

        return await ImportTransactionsInternalAsync(accountId, transactions, accounts, errorIfDuplicateHash);
    }

    private async Task<int> ImportTransactionsInternalAsync(string accountId, IReadOnlyList<FIIITransaction> transactions, IReadOnlyList<AccountSingle> accounts, bool errorIfDuplicateHash)
    {
        var added = 0;

        foreach (var tx in transactions)
        {
            var isDeposit = tx.Amount >= 0m;
            var transactionDate = tx.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            // Description comes from the QFX NAME field (same as data-importer "description" role).
            // Fallback to "(empty description)" per data-importer EmptyDescription convention.
            var description =
                !string.IsNullOrWhiteSpace(tx.Name)
                    ? tx.Name
                    : "(empty description)";

            // Determine opposing account and transaction type
            var (transactionType, sourceName, sourceId, destinationName, destinationId) =
                ResolveTransactionMapping(tx, accountId, accounts, isDeposit);

            var split = new TransactionSplitStore
            {
                Type = transactionType,
                Date = transactionDate,
                Payment_date = transactionDate,
                Order = 0,
                Amount = Math.Abs(tx.Amount).ToString("0.00", CultureInfo.InvariantCulture),
                Description = description,
                Source_id = sourceId,
                Destination_id = destinationId,
                Source_name = sourceName,
                Destination_name = destinationName,
                Notes = string.IsNullOrWhiteSpace(tx.Memo) ? null : tx.Memo,
                External_id = string.IsNullOrWhiteSpace(tx.FitId) ? null : tx.FitId,
                Internal_reference = accountId,
            };

            var payload = new TransactionStore
            {
                Error_if_duplicate_hash = errorIfDuplicateHash,
                Apply_rules = true,
                Fire_webhooks = true,
                Transactions = [split],
            };

            _ = await _client!.StoreTransactionAsync(null, payload);
            added++;
        }

        return added;
    }

    // ── Transfer / account resolution ─────────────────────────────────────────

    /// <summary>
    /// Patterns used to extract an account name from the NAME field when Memo is "Transferred".
    /// E.g. "Deposit from Savings", "Withdrawal to Chequing", "Transfer from TFSA".
    /// </summary>
    private static readonly Regex TransferNamePattern = new(
        @"^(?:Deposit|Transfer|Withdrawal|Payment)\s+(?:from|to)\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Resolves the transaction type and source/destination fields based on the Memo field.
    /// Follows the same conventions as firefly-iii/data-importer:
    /// - Memo is treated as the opposing account name (like the "opposing-name" CSV role).
    /// - "To " and "From " prefixes are stripped from the opposing name.
    /// - When Memo is "Transferred", the Name field is parsed to identify an existing account
    ///   and the transaction becomes a Transfer (same as data-importer's Accounts task which
    ///   detects transfers when both source and destination are asset accounts).
    /// - When the opposing account name is empty, "(no name)" is used per EmptyAccounts convention.
    /// </summary>
    private static (TransactionTypeProperty type, string? sourceName, string? sourceId, string? destinationName, string? destinationId)
        ResolveTransactionMapping(
            FIIITransaction tx,
            string accountId,
            IReadOnlyList<AccountSingle> accounts,
            bool isDeposit)
    {
        // Check if this is an inter-account transfer (Memo == "Transferred")
        if (string.Equals(tx.Memo?.Trim(), "Transferred", StringComparison.OrdinalIgnoreCase))
        {
            var opposingAccountId = TryMatchAccountFromName(tx.Name, accounts);
            if (opposingAccountId != null)
            {
                // Both accounts are asset accounts → Transfer (same logic as data-importer Accounts task)
                if (isDeposit)
                {
                    // Money coming in: source is the matched account, destination is our account
                    return (TransactionTypeProperty.Transfer, null, opposingAccountId, null, accountId);
                }
                else
                {
                    // Money going out: source is our account, destination is the matched account
                    return (TransactionTypeProperty.Transfer, null, accountId, null, opposingAccountId);
                }
            }

            // Couldn't match an account by ID — try to extract a name from the pattern
            // and submit it as a name so Firefly III can resolve or create it
            var nameMatch = TransferNamePattern.Match(tx.Name ?? string.Empty);
            if (nameMatch.Success)
            {
                var extractedName = nameMatch.Groups[1].Value.Trim();
                if (isDeposit)
                    return (TransactionTypeProperty.Transfer, extractedName, null, null, accountId);
                else
                    return (TransactionTypeProperty.Transfer, null, accountId, extractedName, null);
            }
        }

        // Normal transaction: Memo is the opposing account name (like "opposing-name" role in data-importer)
        var opposingName = CleanAccountName(tx.Memo);

        // Per data-importer EmptyAccounts convention: if opposing name is empty, use "(no name)"
        opposingName ??= "(no name)";

        if (isDeposit)
        {
            // Deposit: source is the opposing account (revenue), destination is our asset account
            return (TransactionTypeProperty.Deposit, opposingName, null, null, accountId);
        }
        else
        {
            // Withdrawal: source is our asset account, destination is the opposing account (expense)
            return (TransactionTypeProperty.Withdrawal, null, accountId, opposingName, null);
        }
    }

    /// <summary>
    /// Cleans an account name from the Memo field.
    /// The data-importer itself passes opposing-name values through CleanString (trim + remove newlines)
    /// and relies on user mapping for name normalization. Since we don't have a mapping step,
    /// we strip common OFX/QFX prefixes that banks add to memo fields:
    /// - "To " prefix (e.g. "To Savings" → "Savings")
    /// - "From " prefix (e.g. "From Chequing" → "Chequing")
    /// </summary>
    private static string? CleanAccountName(string? memo)
    {
        if (string.IsNullOrWhiteSpace(memo))
            return null;

        var cleaned = memo.Trim();

        // Remove common prefixes (case-insensitive) that banks add to QFX memo fields
        if (cleaned.StartsWith("To ", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..].Trim();
        else if (cleaned.StartsWith("From ", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[5..].Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// Attempts to match an account from the Name field when Memo is "Transferred".
    /// Parses patterns like "Deposit from Savings" and matches against known accounts.
    /// Returns the account ID if found, null otherwise.
    /// </summary>
    private static string? TryMatchAccountFromName(string? name, IReadOnlyList<AccountSingle> accounts)
    {
        if (string.IsNullOrWhiteSpace(name) || accounts.Count == 0)
            return null;

        var match = TransferNamePattern.Match(name);
        if (!match.Success)
            return null;

        var extractedName = match.Groups[1].Value.Trim();

        // Try exact match first (case-insensitive)
        var matched = accounts.FirstOrDefault(a =>
            string.Equals(a.Data?.Attributes?.Name, extractedName, StringComparison.OrdinalIgnoreCase));

        // Try contains match as fallback (e.g. "Savings" matches "My Savings Account")
        matched ??= accounts.FirstOrDefault(a =>
            a.Data?.Attributes?.Name?.Contains(extractedName, StringComparison.OrdinalIgnoreCase) == true);

        return matched?.Data?.Id;
    }

    public void Dispose()
    {
        DisposeOwned();
        GC.SuppressFinalize(this);
    }

    private void EnsureConfigured()
    {
        if (_client is null)
            throw new InvalidOperationException("Service is not configured. Call Configure() or provide a Client instance.");
    }

    private void DisposeOwned()
    {
        _ownedHttpClient?.Dispose();
        _ownedHttpClient = null;
        _client = null;
    }
}
