using QfxWatcher.Models;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QfxWatcher.Services;

/// <summary>
/// Client for the Firefly III REST API.
/// Documentation: https://api-docs.firefly-iii.org/
/// </summary>
/// <remarks>
/// This service is registered as a singleton in <see cref="App"/> and therefore
/// owns a single <see cref="HttpClient"/> instance for the application lifetime,
/// which is the recommended pattern when IHttpClientFactory is not available.
/// </remarks>
public class FireflyIIIService : IDisposable
{
    private HttpClient _http = CreateHttpClient(ignoreSslCertificateValidation: false);
    private bool _ignoreSslCertificateValidation;

    // JSON options shared across all calls
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Configuration ─────────────────────────────────────────────────────────

    public void Configure(string serverUrl, string apiKey, bool ignoreSslCertificateValidation = false)
    {
        if (_ignoreSslCertificateValidation != ignoreSslCertificateValidation)
        {
            _http.Dispose();
            _http = CreateHttpClient(ignoreSslCertificateValidation);
            _ignoreSslCertificateValidation = ignoreSslCertificateValidation;
        }

        var baseUrl = serverUrl.TrimEnd('/');
        _http.BaseAddress = new Uri(baseUrl + "/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static HttpClient CreateHttpClient(bool ignoreSslCertificateValidation)
    {
        var handler = new HttpClientHandler();
        if (ignoreSslCertificateValidation)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        return new HttpClient(handler, disposeHandler: true);
    }

    // ── Accounts ──────────────────────────────────────────────────────────────

    /// <summary>Returns all active asset accounts, following pagination.</summary>
    public async Task<IReadOnlyList<FireflyAccount>> GetAccountsAsync(
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var accounts = new List<FireflyAccount>();
        int page = 1;

        while (true)
        {
            using var response = await _http.GetAsync(
                $"api/v1/accounts?type=asset&page={page}", ct);
            response.EnsureSuccessStatusCode();

            var result = await ReadJsonOrThrowAsync<AccountListResponse>(
                response, "api/v1/accounts", ct);

            if (result?.Data == null || result.Data.Count == 0)
                break;

            foreach (var item in result.Data)
            {
                if (item.Attributes is { } attr)
                    accounts.Add(new FireflyAccount
                    {
                        Id     = item.Id,
                        Name   = attr.Name ?? string.Empty,
                        Type   = attr.Type ?? string.Empty,
                        Active = attr.Active,
                    });
            }

            var pagination = result.Meta?.Pagination;
            if (pagination == null || page >= pagination.TotalPages)
                break;

            page++;
        }

        return accounts;
    }

    // ── Transaction import ────────────────────────────────────────────────────

    /// <summary>
    /// Imports transactions into the given account.
    /// Returns the number of transactions that were actually added (duplicates skipped).
    /// </summary>
    public async Task<int> ImportTransactionsAsync(
        string accountId,
        IEnumerable<QfxTransaction> transactions,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        int imported = 0;
        foreach (var t in transactions)
        {
            bool isDebit   = t.Amount <= 0;
            var  amount    = Math.Abs(t.Amount).ToString("F2", CultureInfo.InvariantCulture);
            var  description = !string.IsNullOrWhiteSpace(t.Name) ? t.Name
                             : !string.IsNullOrWhiteSpace(t.Memo) ? t.Memo
                             : "Import";

            var split = new TransactionSplit
            {
                Type            = isDebit ? "withdrawal" : "deposit",
                Date            = t.Date.ToString("yyyy-MM-dd"),
                Amount          = amount,
                Description     = description,
                Notes           = t.Memo,
                ExternalId      = t.FitId,
                SourceId        = isDebit  ? accountId : null,
                SourceName      = isDebit  ? null      : "Unknown Revenue",
                DestinationId   = isDebit  ? null      : accountId,
                DestinationName = isDebit  ? "Unknown Expense" : null,
            };

            var body = new
            {
                error_if_duplicate_hash = true,
                apply_rules             = true,
                transactions            = new[] { split },
            };

            using var response = await _http.PostAsJsonAsync("api/v1/transactions", body, ct);

            if (response.IsSuccessStatusCode)
            {
                imported++;
            }
            else if ((int)response.StatusCode == 422)
            {
                // Duplicate transaction – skip silently
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }
        }

        return imported;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureConfigured()
    {
        if (_http.BaseAddress == null)
            throw new InvalidOperationException(
                "Not configured. Call Configure first.");
    }

    private async Task<T?> ReadJsonOrThrowAsync<T>(
        HttpResponseMessage response,
        string endpoint,
        CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            var snippet = raw.Length > 120 ? raw[..120] + "..." : raw;
            throw new InvalidOperationException(
                $"Expected JSON from '{endpoint}', but received non-JSON content. " +
                "Check the Server URL and API routing. " +
                $"Response starts with: {snippet}");
        }
    }

    public void Dispose() => _http.Dispose();

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed class AccountListResponse
    {
        public List<AccountItem>? Data { get; set; }
        public AccountMeta?       Meta { get; set; }
    }

    private sealed class AccountItem
    {
        public string             Id         { get; set; } = string.Empty;
        public AccountAttributes? Attributes { get; set; }
    }

    private sealed class AccountAttributes
    {
        public string? Name   { get; set; }
        public string? Type   { get; set; }
        public bool    Active { get; set; } = true;
    }

    private sealed class AccountMeta
    {
        public PaginationData? Pagination { get; set; }
    }

    private sealed class PaginationData
    {
        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }
    }

    private sealed class TransactionSplit
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public string Amount { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("external_id")]
        public string? ExternalId { get; set; }

        [JsonPropertyName("source_id")]
        public string? SourceId { get; set; }

        [JsonPropertyName("source_name")]
        public string? SourceName { get; set; }

        [JsonPropertyName("destination_id")]
        public string? DestinationId { get; set; }

        [JsonPropertyName("destination_name")]
        public string? DestinationName { get; set; }
    }
}
