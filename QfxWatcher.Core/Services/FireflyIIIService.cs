using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using System.Globalization;
using System.Net.Http.Headers;

namespace QfxWatcher.Services;

/// <summary>
/// Service adapter that maps the app's backend operations to Firefly III.
/// </summary>
public sealed class FireflyIIIService : IDisposable
{
    private HttpClient? _httpClient;
    private Client? _client;
    private string _baseUrl = string.Empty;

    public void Configure(string serverUrl, bool ignoreSslCertificateValidation = false)
    {
        DisposeClient();

        _baseUrl = serverUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_baseUrl))
            return;

        var handler = new HttpClientHandler();
        if (ignoreSslCertificateValidation)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl, UriKind.Absolute)
        };

        _client = new Client(_httpClient)
        {
            BaseUrl = _baseUrl.TrimEnd('/') + "/api/"
        };
    }

    public async Task<bool> LoginAsync(string password)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(password))
            return false;

        _httpClient!.DefaultRequestHeaders.Authorization =
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
            _httpClient.DefaultRequestHeaders.Authorization = null;
            return false;
        }
    }

    public async Task<IReadOnlyList<AccountSingle>> GetAccountsAsync()
    {
        EnsureConfigured();
        EnsureAuthenticated();

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

    public async Task<int> ImportTransactionsAsync(string accountId, IReadOnlyList<QfxTransaction> transactions, bool errorIfDuplicateHash = false)
    {
        EnsureConfigured();
        EnsureAuthenticated();

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
        DisposeClient();
        GC.SuppressFinalize(this);
    }

    private void EnsureConfigured()
    {
        if (_client == null || _httpClient == null || string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Service is not configured. Call Configure() first.");
    }

    private void EnsureAuthenticated()
    {
        if (_httpClient?.DefaultRequestHeaders.Authorization == null)
            throw new InvalidOperationException("Not authenticated. Call LoginAsync() first.");
    }

    private void DisposeClient()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _client = null;
    }
}
