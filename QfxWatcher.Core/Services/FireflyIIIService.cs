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
    /// Invoked when a transaction fails with a 422 validation error.
    /// Parameters: the failing transaction and the response body.
    /// </summary>
    public Action<FIIITransaction, string>? OnTransactionError { get; set; }

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

    /// <summary>
    /// Fetches all asset accounts and liability accounts from Firefly III.
    /// Per data-importer rules, transfers can occur between asset accounts
    /// or between asset accounts and liabilities.
    /// </summary>
    public async Task<IReadOnlyList<AccountSingle>> GetTransferableAccountsAsync()
    {
        EnsureConfigured();

        var assetResponse = await _client!.ListAccountAsync(
            x_Trace_Id: null,
            limit: 200,
            page: 1,
            start: null,
            end: null,
            date: null,
            type: AccountTypeFilter.Asset);

        var liabilityResponse = await _client!.ListAccountAsync(
            x_Trace_Id: null,
            limit: 200,
            page: 1,
            start: null,
            end: null,
            date: null,
            type: AccountTypeFilter.Liability);

        var accounts = new List<AccountSingle>();
        accounts.AddRange(assetResponse.Data.Select(a => new AccountSingle { Data = a }));
        accounts.AddRange(liabilityResponse.Data.Select(a => new AccountSingle { Data = a }));
        return accounts;
    }

    public async Task<int> ImportTransactionsAsync(string accountId, IReadOnlyList<FIIITransaction> transactions, bool errorIfDuplicateHash = false)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(accountId) || transactions.Count == 0)
            return 0;

        // Pre-fetch asset + liability accounts so we can match transfer targets by name,
        // IBAN, or account number (per data-importer transfer detection rules)
        IReadOnlyList<AccountSingle> accounts;
        try
        {
            accounts = await GetTransferableAccountsAsync();
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
                Notes = tx.Notes ?? (string.IsNullOrWhiteSpace(tx.Memo) ? null : tx.Memo),
                External_id = string.IsNullOrWhiteSpace(tx.FitId) ? null : tx.FitId,
                Internal_reference = accountId,
                Category_name = string.IsNullOrWhiteSpace(tx.CategoryName) ? null : tx.CategoryName,
            };

            var payload = new TransactionStore
            {
                Error_if_duplicate_hash = errorIfDuplicateHash,
                Apply_rules = true,
                Fire_webhooks = true,
                Transactions = [split],
            };

            try
            {
                _ = await _client!.StoreTransactionAsync(null, payload);
                added++;
            }
            catch (ApiException ex) when (ex.StatusCode == 422)
            {
                OnTransactionError?.Invoke(tx, ex.Response ?? ex.Message);
            }
        }

        return added;
    }

    // ── Transfer / account resolution ─────────────────────────────────────────

    /// <summary>
    /// Patterns used to extract an account name from the NAME field when a transfer is detected.
    /// E.g. "Deposit from Savings", "Withdrawal to Chequing", "Transfer from TFSA".
    /// </summary>
    private static readonly Regex TransferNamePattern = new(
        @"^(?:Deposit|Transfer|Withdrawal|Payment)\s+(?:from|to)\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Common memo patterns that indicate an inter-account transfer beyond just "Transferred".
    /// Banks use varying terminology; these are the most common seen in Canadian/US OFX files.
    /// </summary>
    private static readonly Regex TransferMemoPattern = new(
        @"^(?:Transferred|Online Transfer|Internet Transfer|e-Transfer|Interac Transfer|Internal Transfer|Transfer|TFR|Funds Transfer)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Determines whether a transaction should be treated as a transfer.
    /// Uses multiple signals per the data-importer approach:
    /// 1. OFX TRNTYPE == "XFER" (standard OFX transfer type)
    /// 2. Memo matches known transfer patterns (like data-importer "opposing-name" role detection)
    /// 3. BANKACCTTO/BANKACCTFROM account number present in the transaction
    /// </summary>
    private static bool IsTransferTransaction(FIIITransaction tx)
    {
        // Signal 1: OFX standard TRNTYPE for transfers
        if (string.Equals(tx.TransactionType?.Trim(), "XFER", StringComparison.OrdinalIgnoreCase))
            return true;

        // Signal 2: Memo matches known transfer patterns
        if (!string.IsNullOrWhiteSpace(tx.Memo) && TransferMemoPattern.IsMatch(tx.Memo.Trim()))
            return true;

        // Signal 3: Opposing account number is present (bank included BANKACCTTO/BANKACCTFROM)
        if (!string.IsNullOrWhiteSpace(tx.OpposingAccountNumber))
            return true;

        return false;
    }

    /// <summary>
    /// Resolves the transaction type and source/destination fields.
    /// Follows the same conventions as firefly-iii/data-importer:
    /// - Firefly III determines transaction type based on source and destination accounts.
    ///   Both must be recognized as asset accounts or liabilities for it to become a Transfer.
    /// - Memo is treated as the opposing account name (like the "opposing-name" CSV role).
    /// - "To " and "From " prefixes are stripped from the opposing name.
    /// - Transfer detection uses TRNTYPE=XFER, Memo patterns, and BANKACCTTO account numbers.
    /// - Account matching by account number/IBAN is the strongest signal (per data-importer docs).
    /// - When the opposing account name is empty, "(no name)" is used per EmptyAccounts convention.
    /// </summary>
    private static (TransactionTypeProperty type, string? sourceName, string? sourceId, string? destinationName, string? destinationId)
        ResolveTransactionMapping(
            FIIITransaction tx,
            string accountId,
            IReadOnlyList<AccountSingle> accounts,
            bool isDeposit)
    {
        // Check if this transaction looks like an inter-account transfer
        if (IsTransferTransaction(tx))
        {
            // Strategy 1: Match by account number from BANKACCTTO/BANKACCTFROM
            // This is the strongest signal per data-importer docs ("IBAN and account numbers")
            if (!string.IsNullOrWhiteSpace(tx.OpposingAccountNumber))
            {
                var opposingAccountId = TryMatchAccountByNumber(tx.OpposingAccountNumber, accounts);
                if (opposingAccountId != null)
                {
                    if (isDeposit)
                        return (TransactionTypeProperty.Transfer, null, opposingAccountId, null, accountId);
                    else
                        return (TransactionTypeProperty.Transfer, null, accountId, null, opposingAccountId);
                }
            }

            // Strategy 2: Match by name extracted from the NAME field pattern
            var opposingAccountIdByName = TryMatchAccountFromName(tx.Name, accounts);
            if (opposingAccountIdByName != null)
            {
                // Both accounts are asset/liability accounts → Transfer
                if (isDeposit)
                    return (TransactionTypeProperty.Transfer, null, opposingAccountIdByName, null, accountId);
                else
                    return (TransactionTypeProperty.Transfer, null, accountId, null, opposingAccountIdByName);
            }

            // Strategy 3: Match by Memo content against account names
            // Some banks put the opposing account name directly in the Memo field
            if (!string.IsNullOrWhiteSpace(tx.Memo) && !TransferMemoPattern.IsMatch(tx.Memo.Trim()))
            {
                // Memo is not a generic transfer keyword — it might be an account name
                var cleanedMemo = CleanAccountName(tx.Memo);
                if (!string.IsNullOrWhiteSpace(cleanedMemo))
                {
                    var memoMatchId = TryMatchAccountByName(cleanedMemo, accounts);
                    if (memoMatchId != null)
                    {
                        if (isDeposit)
                            return (TransactionTypeProperty.Transfer, null, memoMatchId, null, accountId);
                        else
                            return (TransactionTypeProperty.Transfer, null, accountId, null, memoMatchId);
                    }
                }
            }

            // Strategy 4: Extract name from pattern and submit it so Firefly III can resolve/create
            var nameMatch = TransferNamePattern.Match(tx.Name ?? string.Empty);
            if (nameMatch.Success)
            {
                var extractedName = nameMatch.Groups[1].Value.Trim();
                if (isDeposit)
                    return (TransactionTypeProperty.Transfer, extractedName, null, null, accountId);
                else
                    return (TransactionTypeProperty.Transfer, null, accountId, extractedName, null);
            }

            // Strategy 5: TRNTYPE=XFER or account number present but couldn't match —
            // still submit as Transfer with whatever info we have, let Firefly III resolve it
            if (string.Equals(tx.TransactionType?.Trim(), "XFER", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(tx.OpposingAccountNumber))
            {
                // Use the opposing account number as the name hint for Firefly III
                var hint = !string.IsNullOrWhiteSpace(tx.OpposingAccountNumber)
                    ? tx.OpposingAccountNumber
                    : CleanAccountName(tx.Memo) ?? "(no name)";

                if (isDeposit)
                    return (TransactionTypeProperty.Transfer, hint, null, null, accountId);
                else
                    return (TransactionTypeProperty.Transfer, null, accountId, hint, null);
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
            // ATM/cash withdrawals go to Firefly III's special "(cash)" destination account
            // per Firefly III's cash tracking conventions (How to track cash).
            var trnType = tx.TransactionType?.Trim();
            if (string.Equals(trnType, "ATM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trnType, "CASH", StringComparison.OrdinalIgnoreCase))
            {
                return (TransactionTypeProperty.Withdrawal, null, accountId, "(cash)", null);
            }

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
    /// Attempts to match an account by its account number or IBAN.
    /// Per data-importer docs: "If your file contains IBANs or account numbers,
    /// definitely use them to link them to your asset accounts."
    /// </summary>
    private static string? TryMatchAccountByNumber(string accountNumber, IReadOnlyList<AccountSingle> accounts)
    {
        if (string.IsNullOrWhiteSpace(accountNumber) || accounts.Count == 0)
            return null;

        var trimmed = accountNumber.Trim();

        // Try exact match on account_number field
        var matched = accounts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Data?.Attributes?.Account_number) &&
            string.Equals(a.Data.Attributes.Account_number.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

        // Try IBAN match (some banks provide full or partial IBANs)
        matched ??= accounts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Data?.Attributes?.Iban) &&
            (string.Equals(a.Data.Attributes.Iban.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
             a.Data.Attributes.Iban.Trim().EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)));

        // Try suffix match on account_number (some banks truncate or use last N digits)
        matched ??= accounts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Data?.Attributes?.Account_number) &&
            trimmed.Length >= 4 &&
            a.Data.Attributes.Account_number.Trim().EndsWith(trimmed, StringComparison.OrdinalIgnoreCase));

        return matched?.Data?.Id;
    }

    /// <summary>
    /// Attempts to match an account by name (direct search against known accounts).
    /// </summary>
    private static string? TryMatchAccountByName(string name, IReadOnlyList<AccountSingle> accounts)
    {
        if (string.IsNullOrWhiteSpace(name) || accounts.Count == 0)
            return null;

        var trimmed = name.Trim();

        // Exact match first (case-insensitive)
        var matched = accounts.FirstOrDefault(a =>
            string.Equals(a.Data?.Attributes?.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        // Contains match as fallback (e.g. "Savings" matches "My Savings Account")
        matched ??= accounts.FirstOrDefault(a =>
            a.Data?.Attributes?.Name?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true);

        return matched?.Data?.Id;
    }

    /// <summary>
    /// Attempts to match an account from the Name field by parsing transfer patterns.
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
        return TryMatchAccountByName(extractedName, accounts);
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
