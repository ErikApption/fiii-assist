using QfxWatcher.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QfxWatcher.Services;

/// <summary>
/// Client for the Actual Budget REST API.
/// Documentation: https://actualbudget.org/docs/api/
/// </summary>
/// <remarks>
/// This service is registered as a singleton in <see cref="App"/> and therefore
/// owns a single <see cref="HttpClient"/> instance for the application lifetime,
/// which is the recommended pattern when IHttpClientFactory is not available.
/// </remarks>
public class ActualBudgetService : IDisposable
{
    private readonly HttpClient _http = new();
    private string? _token;

    // JSON options shared across all calls
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Configuration ─────────────────────────────────────────────────────────

    public void Configure(string serverUrl)
    {
        var baseUrl = serverUrl.TrimEnd('/');
        _http.BaseAddress = new Uri(baseUrl + "/");
        _token = null;
    }

    // ── Authentication ────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates with the Actual Budget server using the given password.
    /// Returns true on success, false if the password is wrong.
    /// Throws <see cref="HttpRequestException"/> on connection errors.
    /// </summary>
    public async Task<bool> LoginAsync(string password, CancellationToken ct = default)
    {
        var body = new { password };
        using var response = await _http.PostAsJsonAsync("account/login", body, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content
            .ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);

        if (result?.Status == "ok" && !string.IsNullOrWhiteSpace(result.Data?.Token))
        {
            _token = result.Data.Token;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
            return true;
        }

        return false;
    }

    // ── Accounts ──────────────────────────────────────────────────────────────

    /// <summary>Returns all open (non-closed) accounts.</summary>
    public async Task<IReadOnlyList<ActualAccount>> GetAccountsAsync(
        CancellationToken ct = default)
    {
        EnsureAuthenticated();

        using var response = await _http.GetAsync("api/accounts", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ApiListResponse<ActualAccount>>(JsonOptions, ct);

        return result?.Data ?? [];
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
        EnsureAuthenticated();

        var payload = transactions.Select(t => new
        {
            account       = accountId,
            date          = t.Date.ToString("yyyy-MM-dd"),
            amount        = t.AmountMilliunits,
            payee_name    = t.Name,
            imported_id   = t.FitId,
            notes         = t.Memo,
        }).ToArray();

        var body = new { transactions = payload };

        using var response = await _http.PostAsJsonAsync(
            $"api/accounts/{accountId}/import-transactions", body, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ImportResponse>(JsonOptions, ct);

        return result?.Data?.Added ?? payload.Length;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureAuthenticated()
    {
        if (_token == null)
            throw new InvalidOperationException(
                "Not authenticated. Call LoginAsync first.");
    }

    public void Dispose() => _http.Dispose();

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed class LoginResponse
    {
        public string? Status { get; set; }
        public LoginData? Data { get; set; }
    }

    private sealed class LoginData
    {
        public string? Token { get; set; }
    }

    private sealed class ApiListResponse<T>
    {
        public string? Status { get; set; }
        public List<T>? Data { get; set; }
    }

    private sealed class ImportResponse
    {
        public string? Status { get; set; }
        public ImportData? Data { get; set; }
    }

    private sealed class ImportData
    {
        public int Added { get; set; }
        public int Updated { get; set; }
    }
}
