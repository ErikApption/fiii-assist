using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using System.Globalization;
using System.Net.Http.Headers;

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

        var added = 0;

        foreach (var tx in transactions)
        {
            var isDeposit = tx.Amount >= 0m;
            var transactionDate = tx.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var cleanDescription =
                !string.IsNullOrWhiteSpace(tx.Name)
                    ? tx.Name
                    : !string.IsNullOrWhiteSpace(tx.Memo)
                        ? tx.Memo
                        : !string.IsNullOrWhiteSpace(tx.FitId)
                            ? tx.FitId
                            : "QFX import";

            var split = new TransactionSplitStore
            {
                Type = isDeposit ? TransactionTypeProperty.Deposit : TransactionTypeProperty.Withdrawal,
                Date = transactionDate,
                Payment_date = transactionDate,
                Order = 0,
                Amount = Math.Abs(tx.Amount).ToString("0.00", CultureInfo.InvariantCulture),
                Description = cleanDescription,
                Source_id = isDeposit ? null : accountId,
                Destination_id = isDeposit ? accountId : null,
                Source_name = isDeposit ? cleanDescription : null,
                Destination_name = isDeposit ? null : cleanDescription,
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
