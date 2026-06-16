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
    /// Invoked after each transaction is processed during an import.
    /// Parameters: (transaction, result) where result indicates success, skipped, or failed.
    /// </summary>
    public Action<FIIITransaction, ImportTransactionResult>? OnTransactionProcessed { get; set; }

    /// <summary>
    /// Gets the underlying Firefly III client instance, or null if not configured.
    /// </summary>
    public Client? Client => _client;

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
    /// Updates the account_number field on a Firefly III account.
    /// Used to persist the QFX ACCTID after the user manually maps an account,
    /// so future imports can auto-match.
    /// </summary>
    public async Task UpdateAccountNumberAsync(string accountId, string accountNumber)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accountNumber))
            return;

        // Fetch the current account to get required fields (Name is required for update)
        var accounts = await GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => a.Data.Id == accountId);
        if (account is null)
            return;

        var update = new AccountUpdate
        {
            Name = account.Data.Attributes.Name,
            Account_number = accountNumber,
        };

        await _client!.UpdateAccountAsync(null, accountId, update);
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

    public async Task<int> ImportTransactionsAsync(string accountId, IReadOnlyList<FIIITransaction> transactions, bool errorIfDuplicateHash = false, bool skipDuplicatesByContent = false, bool useBatchMode = false, HashSet<string>? existingExternalIds = null)
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

        return await ImportTransactionsInternalAsync(accountId, transactions, accounts, errorIfDuplicateHash, skipDuplicatesByContent, useBatchMode, existingExternalIds);
    }

    /// <summary>
    /// Gets the set of external IDs (FitIds) that already exist for the given account
    /// within the specified date range. Used for client-side duplicate detection.
    /// </summary>
    public async Task<HashSet<string>> GetExistingExternalIdsAsync(string accountId, DateOnly startDate, DateOnly endDate)
    {
        EnsureConfigured();

        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(accountId))
            return existingIds;

        try
        {
            var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var result = await _client!.ListTransactionByAccountAsync(
                    x_Trace_Id: null,
                    limit: pageSize,
                    page: page,
                    id: accountId,
                    start: start,
                    end: end,
                    type: TransactionTypeFilter.All);

                if (result?.Data == null || result.Data.Count == 0)
                    break;

                foreach (var txGroup in result.Data)
                {
                    if (txGroup?.Attributes?.Transactions == null)
                        continue;

                    foreach (var split in txGroup.Attributes.Transactions)
                    {
                        if (!string.IsNullOrWhiteSpace(split.External_id))
                            existingIds.Add(split.External_id);
                    }
                }

                // If we got fewer results than the page size, we've reached the end
                if (result.Data.Count < pageSize)
                    break;

                page++;
            }
        }
        catch
        {
            // If fetching fails, return empty set — duplicates will be handled server-side
        }

        return existingIds;
    }

    /// <summary>
    /// Fetches existing transactions for the given account within the date range and builds
    /// content-based deduplication keys (date + absolute amount + source_id + destination_id).
    /// Used to detect duplicates created by other tools that did not set external_id correctly.
    /// </summary>
    public async Task<HashSet<string>> GetExistingContentKeysAsync(string accountId, DateOnly startDate, DateOnly endDate)
    {
        EnsureConfigured();

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(accountId))
            return keys;

        try
        {
            var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var result = await _client!.ListTransactionByAccountAsync(
                    x_Trace_Id: null,
                    limit: pageSize,
                    page: page,
                    id: accountId,
                    start: start,
                    end: end,
                    type: TransactionTypeFilter.All);

                if (result?.Data == null || result.Data.Count == 0)
                    break;

                foreach (var txGroup in result.Data)
                {
                    if (txGroup?.Attributes?.Transactions == null)
                        continue;

                    foreach (var split in txGroup.Attributes.Transactions)
                    {
                        var date = DateOnly.FromDateTime(split.Date.DateTime);
                        var amount = decimal.TryParse(split.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                            ? Math.Abs(a)
                            : 0m;

                        keys.Add(BuildContentKey(date, amount, split.Source_id, split.Destination_id));
                    }
                }

                if (result.Data.Count < pageSize)
                    break;

                page++;
            }
        }
        catch
        {
            // If fetching fails, return empty set — other dedup mechanisms still apply
        }

        return keys;
    }

    /// <summary>
    /// Builds a content-based deduplication key: date + absolute amount + source account ID + destination account ID.
    /// </summary>
    private static string BuildContentKey(DateOnly date, decimal amount, string? sourceId, string? destinationId)
    {
        return $"{date:yyyyMMdd}|{amount:0.00}|{sourceId ?? ""}|{destinationId ?? ""}";
    }

    private async Task<int> ImportTransactionsInternalAsync(string accountId, IReadOnlyList<FIIITransaction> transactions, IReadOnlyList<AccountSingle> accounts, bool errorIfDuplicateHash, bool skipDuplicatesByContent, bool useBatchMode, HashSet<string>? existingExternalIds)
    {
        var added = 0;

        if (transactions.Count == 0)
            return added;

        // Pre-fetch existing transfers for this account to detect duplicates across import sources.
        // Different sources use different external_ids for the same real
        // transaction, so we need to match by date + amount + opposing account.
        HashSet<string>? existingTransferKeys = null;

        try
        {
            var dates = transactions.Select(t => t.Date).ToList();
            existingTransferKeys = await GetExistingTransferKeysAsync(accountId, dates.Min(), dates.Max());
        }
        catch
        {
            // If lookup fails, proceed without transfer dedup — server-side hash dedup still applies
        }
        
        // Content-based dedup: fetch existing transactions and build keys from
        // date + amount + source + destination to catch duplicates from other tools.
        HashSet<string>? existingContentKeys = null;
        if (skipDuplicatesByContent)
        {
            try
            {
                var dates = transactions.Select(t => t.Date).ToList();
                existingContentKeys = await GetExistingContentKeysAsync(accountId, dates.Min(), dates.Max());
            }
            catch
            {
                // If lookup fails, proceed without content dedup
            }
        }

        foreach (var tx in transactions)
        {
            // Skip by external ID (FitId) if caller provided a set of known IDs
            if (existingExternalIds != null
                && !string.IsNullOrWhiteSpace(tx.FitId)
                && existingExternalIds.Contains(tx.FitId))
            {
                OnTransactionProcessed?.Invoke(tx, ImportTransactionResult.Skipped);
                continue;
            }

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

            // Skip duplicate transfers: if an existing transfer with the same date, amount,
            // and opposing account already exists, don't create another one.
            // This prevents duplicates when the same movement is imported from different sources
            // (e.g. QFX bank file and Actual Budget export) which have different external_ids.
            // Also handles the case where we submit a Withdrawal but Firefly III will auto-promote
            // it to a Transfer (because destination_name matches a known asset account).
            if (existingTransferKeys != null)
            {
                string? opposingId = null;
                if (transactionType == TransactionTypeProperty.Transfer)
                {
                    opposingId = isDeposit ? sourceId : destinationId;
                }
                else
                {
                    // Even for withdrawals/deposits, Firefly III may auto-promote to Transfer
                    // if the opposing name matches an asset account. Check for that.
                    var nameToCheck = isDeposit ? sourceName : destinationName;
                    if (!string.IsNullOrWhiteSpace(nameToCheck))
                        opposingId = TryMatchAccountByName(nameToCheck, accounts);
                }

                if (!string.IsNullOrWhiteSpace(opposingId))
                {
                    var key = BuildTransferKey(tx.Date, Math.Abs(tx.Amount), opposingId);
                    if (existingTransferKeys.Contains(key))
                    {
                        // Transfer already exists — skip to avoid duplicate
                        OnTransactionProcessed?.Invoke(tx, ImportTransactionResult.Skipped);
                        continue;
                    }
                }
            }

            // Content-based dedup: skip if a transaction with the same date, amount,
            // source, and destination already exists in Firefly III.
            if (existingContentKeys != null)
            {
                // Resolve source/destination IDs for the content key.
                // For transfers, both IDs are already resolved.
                // For withdrawals/deposits, one side is the account ID and the other is a name
                // that may resolve to an existing account.
                var resolvedSourceId = sourceId ?? (sourceName != null ? TryMatchAccountByName(sourceName, accounts) : null);
                var resolvedDestId = destinationId ?? (destinationName != null ? TryMatchAccountByName(destinationName, accounts) : null);

                var contentKey = BuildContentKey(tx.Date, Math.Abs(tx.Amount), resolvedSourceId, resolvedDestId);
                if (existingContentKeys.Contains(contentKey))
                {
                    // Duplicate by content — skip
                    OnTransactionProcessed?.Invoke(tx, ImportTransactionResult.Skipped);
                    continue;
                }
            }

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
                Batch_submission = useBatchMode,
                Transactions = [split]                
            };

            try
            {
                _ = await _client!.StoreTransactionAsync(null, payload);
                added++;
                OnTransactionProcessed?.Invoke(tx, ImportTransactionResult.Imported);
            }
            catch (ApiException ex) when (ex.StatusCode == 422)
            {
                OnTransactionError?.Invoke(tx, ex.Response ?? ex.Message);
                OnTransactionProcessed?.Invoke(tx, ImportTransactionResult.Failed);
            }
        }
        if (useBatchMode)
        {
            await _client!.FinishBatchAsync(null);
        }

        return added;
    }

    /// <summary>
    /// Builds a deduplication key for a transfer: date + absolute amount + opposing account ID.
    /// </summary>
    private static string BuildTransferKey(DateOnly date, decimal amount, string? opposingAccountId)
    {
        return $"{date:yyyyMMdd}|{amount:0.00}|{opposingAccountId ?? ""}";
    }

    /// <summary>
    /// Fetches existing transfers for an account within the date range and returns
    /// deduplication keys (date + amount + opposing account ID) for each.
    /// </summary>
    private async Task<HashSet<string>> GetExistingTransferKeysAsync(string accountId, DateOnly startDate, DateOnly endDate)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var page = 1;
        const int pageSize = 100;

        while (true)
        {
            var result = await _client!.ListTransactionByAccountAsync(
                x_Trace_Id: null,
                limit: pageSize,
                page: page,
                id: accountId,
                start: start,
                end: end,
                type: TransactionTypeFilter.Transfer);

            if (result?.Data == null || result.Data.Count == 0)
                break;

            foreach (var txGroup in result.Data)
            {
                if (txGroup?.Attributes?.Transactions == null)
                    continue;

                foreach (var split in txGroup.Attributes.Transactions)
                {
                    // Determine opposing account ID relative to our accountId
                    string? opposingId = null;
                    if (split.Source_id == accountId)
                        opposingId = split.Destination_id;
                    else if (split.Destination_id == accountId)
                        opposingId = split.Source_id;

                    if (opposingId == null)
                        continue;

                    var date = DateOnly.FromDateTime(split.Date.DateTime);
                    var amount = decimal.TryParse(split.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        ? Math.Abs(a)
                        : 0m;

                    keys.Add(BuildTransferKey(date, amount, opposingId));
                }
            }

            if (result.Data.Count < pageSize)
                break;

            page++;
        }

        return keys;
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

            // Strategy 4: Extract name from pattern — but ONLY create a transfer if the
            // extracted name matches a known asset/liability account. Otherwise this is likely
            // a regular withdrawal/deposit (e.g. "EFT Withdrawal to Vendor" or "Payment to Company")
            // that merely uses transfer-like wording in the NAME field.
            var nameMatch = TransferNamePattern.Match(tx.Name ?? string.Empty);
            if (nameMatch.Success)
            {
                var extractedName = nameMatch.Groups[1].Value.Trim();
                var matchedId = TryMatchAccountByName(extractedName, accounts);
                if (matchedId != null)
                {
                    if (isDeposit)
                        return (TransactionTypeProperty.Transfer, null, matchedId, null, accountId);
                    else
                        return (TransactionTypeProperty.Transfer, null, accountId, null, matchedId);
                }
                // Name didn't match a known account — fall through to treat as normal transaction
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

        // Normal transaction: determine the opposing account name.
        // For DEBIT and CREDIT transactions, use the Name field (payee/merchant) as the
        // opposing account since it more accurately identifies the counterparty.
        // For other types, use the Memo field (like "opposing-name" role in data-importer).
        string? opposingName;
        var trnTypeNorm = tx.TransactionType?.Trim();
        if (string.Equals(trnTypeNorm, "DEBIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trnTypeNorm, "CREDIT", StringComparison.OrdinalIgnoreCase))
            opposingName = CleanAccountName(tx.Name);
        else
            opposingName = CleanAccountName(tx.Memo);

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
            if (string.Equals(trnTypeNorm, "ATM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trnTypeNorm, "CASH", StringComparison.OrdinalIgnoreCase))
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
    /// Applies spelling normalization to handle regional variants (e.g. Chequing/Checking).
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

        // Reverse contains match (e.g. "Tangerine Chequing Account" contains account name "Tangerine Chequing")
        matched ??= accounts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Data?.Attributes?.Name) &&
            trimmed.Contains(a.Data.Attributes.Name, StringComparison.OrdinalIgnoreCase));

        // Normalized match: handle regional spelling variants (Chequing/Checking, Honour/Honor, etc.)
        if (matched == null)
        {
            var normalizedSearch = NormalizeAccountName(trimmed);
            matched = accounts.FirstOrDefault(a =>
            {
                var acctName = a.Data?.Attributes?.Name;
                if (string.IsNullOrWhiteSpace(acctName)) return false;
                var normalizedAcct = NormalizeAccountName(acctName);
                return string.Equals(normalizedAcct, normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || normalizedAcct.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    || normalizedSearch.Contains(normalizedAcct, StringComparison.OrdinalIgnoreCase);
            });
        }

        return matched?.Data?.Id;
    }

    /// <summary>
    /// Normalizes an account name by replacing regional spelling variants with a canonical form.
    /// Canadian banks use "Chequing" while Firefly III users may use "Checking", etc.
    /// </summary>
    private static string NormalizeAccountName(string name)
    {
        // Replace Canadian/British spelling variants with American equivalents for comparison
        var normalized = name;
        normalized = Regex.Replace(normalized, @"\bChequing\b", "Checking", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bCheque\b", "Check", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bSavings\b", "Saving", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bSaving\b", "Saving", RegexOptions.IgnoreCase);
        return normalized;
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
