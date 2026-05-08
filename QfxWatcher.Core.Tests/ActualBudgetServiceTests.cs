using QfxWatcher.Models;
using QfxWatcher.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace QfxWatcher.Core.Tests;

public class ActualBudgetServiceTests
{
    [Fact]
    public async Task GetAccountsAsync_BeforeLogin_ThrowsHttpRequestException()
    {
        using var sut = new FireflyIIIService();
        sut.Configure("http://localhost:12345");

        await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => sut.GetAccountsAsync());
    }

    [Fact]
    public async Task LoginAndAccounts_ValidResponses_ReturnsAccounts()
    {
        using var server = new FakeActualServer();
        using var sut = new FireflyIIIService();

        sut.Configure(server.BaseUrl);

        var loggedIn = await sut.LoginAsync("test-token");
        var accounts = await sut.GetAccountsAsync();

        Assert.True(loggedIn);
        var account = Assert.Single(accounts);
        Assert.Equal("acc-1", account.Data.Id);
        Assert.Equal("Checking", account.Data.Attributes.Name);
        Assert.Equal("Asset", account.Data.Attributes.Type.ToString());
    }

    [Fact]
    public async Task ImportTransactionsAsync_UsesAddedFromResponse()
    {
        using var server = new FakeActualServer();
        using var sut = new FireflyIIIService();

        sut.Configure(server.BaseUrl);
        var loggedIn = await sut.LoginAsync("test-token");

        Assert.True(loggedIn);

        var added = await sut.ImportTransactionsAsync("acc-1",
        [
            new FIIITransaction
            {
                FitId = "fit-1",
                Date = new DateOnly(2025, 5, 2),
                Amount = -10.50m,
                Name = "Groceries",
                Memo = "Weekly",
            }
        ],
        errorIfDuplicateHash: true);

        Assert.Equal(1, added);
    }

    private sealed class FakeActualServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serverTask;

        public string BaseUrl { get; }

        public FakeActualServer()
        {
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";

            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();

            _serverTask = Task.Run(() => ServeAsync(_cts.Token));
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var auth = request.Headers["Authorization"];
                if (!string.Equals(auth, "Bearer test-token", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(response, HttpStatusCode.Unauthorized, new { message = "Unauthenticated" });
                    return;
                }

                if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/api/v1/accounts")
                {
                    await WriteJsonAsync(response, HttpStatusCode.OK, new
                    {
                        data = new[]
                        {
                            new
                            {
                                type = "accounts",
                                id = "acc-1",
                                attributes = new
                                {
                                    name = "Checking",
                                    type = "asset",
                                    active = true
                                }
                            }
                        },
                        meta = new
                        {
                            pagination = new
                            {
                                total = 1,
                                count = 1,
                                per_page = 50,
                                current_page = 1,
                                total_pages = 1
                            }
                        }
                    });
                    return;
                }

                if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/api/v1/transactions")
                {
                    await WriteJsonAsync(response, HttpStatusCode.OK, new
                    {
                        data = new
                        {
                            type = "transactions",
                            id = "tx-1",
                            attributes = new
                            {
                                transactions = Array.Empty<object>()
                            }
                        }
                    });
                    return;
                }

                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
            }
            catch
            {
                if (response.OutputStream.CanWrite)
                {
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.Close();
                }
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload)
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }

        private static int GetFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            try { _serverTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
        }
    }
}
