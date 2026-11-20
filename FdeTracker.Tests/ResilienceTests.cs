using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Tests;

/// <summary>How errors are sorted, how the client waits and retries, and the limits on slow work. A fake clock records every wait, so nothing really sleeps.</summary>
public sealed class ResilienceTests
{
    sealed class Stub(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(++Calls, request, ct);
    }

    const string Good = """{"model":"m","choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":1}}""";

    static HttpResponseMessage Reply(int code, string body = "{}", TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (retryAfter is { } w) r.Headers.RetryAfter = new RetryConditionHeaderValue(w);
        return r;
    }

    static (ILlmClient Client, List<TimeSpan> Waits) Make(Stub stub, int delayMs = 100, int attempts = 3, int total = 60, int timeout = 30, double jitter = 0.5)
    {
        var waits = new List<TimeSpan>();
        var o = new AppOptions
        {
            LlmApiKey = "k", LlmModel = "m", LlmBaseUrl = "http://p.test/v1", LlmRetryDelayMs = delayMs, LlmMaxAttempts = attempts,
            LlmTotalTimeoutSeconds = total, LlmTimeoutSeconds = timeout, Jitter = () => jitter, Sleep = (d, _) => { waits.Add(d); return Task.CompletedTask; },
        };
        return (LlmClientFactory.Create("openai", new HttpClient(stub), o), waits);
    }

    [Theory]
    [InlineData(400, "bad_request", ErrorCategory.Validation, false)]
    [InlineData(413, "too_large", ErrorCategory.Validation, false)]
    [InlineData(401, "auth", ErrorCategory.Permission, false)]
    [InlineData(403, "auth", ErrorCategory.Permission, false)]
    [InlineData(404, "model_not_found", ErrorCategory.Business, false)]
    [InlineData(408, "timeout", ErrorCategory.Transient, true)]
    [InlineData(429, "rate_limited", ErrorCategory.RateLimit, true)]
    [InlineData(500, "server_error", ErrorCategory.Transient, true)]
    [InlineData(503, "server_error", ErrorCategory.Transient, true)]
    [InlineData(418, "provider_error", ErrorCategory.Business, false)]
    public void Each_provider_status_lands_in_one_category_with_a_retry_flag(int code, string kind, ErrorCategory category, bool retryable)
    {
        var e = LlmException.FromStatus(code);
        Assert.Equal((kind, category, retryable), (e.Kind, e.Category, e.Retryable));
    }

    [Theory]
    [InlineData("timeout", ErrorCategory.Transient, true)]
    [InlineData("provider_error", ErrorCategory.Business, false)]
    public void A_caller_that_only_knows_a_kind_still_gets_a_sensible_category(string kind, ErrorCategory category, bool retryable)
    {
        var e = new LlmException(kind, "x");
        Assert.Equal((category, retryable), (e.Category, e.Retryable));
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(418)]
    public async Task Errors_that_asking_again_cannot_fix_are_not_retried(int code)
    {
        var stub = new Stub((_, _, _) => Task.FromResult(Reply(code)));
        var (client, waits) = Make(stub);
        await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(1, stub.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_blocked_answer_is_a_business_error_and_is_not_retried()
    {
        var stub = new Stub((_, _, _) => Task.FromResult(Reply(200, """{"choices":[{"message":{"content":""},"finish_reason":"content_filter"}]}""")));
        var e = await Assert.ThrowsAsync<LlmException>(() => Make(stub).Client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(("content_blocked", ErrorCategory.Business, false), (e.Kind, e.Category, e.Retryable));
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task The_wait_the_provider_asks_for_in_Retry_After_is_the_wait_used()
    {
        var stub = new Stub((n, _, _) => Task.FromResult(n == 1 ? Reply(429, retryAfter: TimeSpan.FromSeconds(2)) : Reply(200, Good)));
        var (client, waits) = Make(stub);
        await client.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task A_retry_hint_in_the_body_is_used_too_the_way_Gemini_sends_it()
    {
        var body = """{"error":{"code":429,"message":"You exceeded your current quota. Please retry in 3.5s.","status":"RESOURCE_EXHAUSTED"}}""";
        var stub = new Stub((n, _, _) => Task.FromResult(n == 1 ? Reply(429, body) : Reply(200, Good)));
        var (client, waits) = Make(stub);
        await client.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(3.5)], waits);
    }

    [Fact]
    public async Task A_wait_longer_than_we_will_sit_through_is_passed_to_the_caller_at_once()
    {
        var stub = new Stub((_, _, _) => Task.FromResult(Reply(429, retryAfter: TimeSpan.FromSeconds(120))));
        var (client, waits) = Make(stub);
        var e = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(120), e.RetryAfter);
        Assert.Equal(1, stub.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Without_a_hint_the_wait_doubles_each_time()
    {
        var stub = new Stub((_, _, _) => Task.FromResult(Reply(500)));
        var (client, waits) = Make(stub, attempts: 4);
        await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal(4, stub.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)], waits);
    }

    [Theory]
    [InlineData(0.0, 75.0)]
    [InlineData(1.0, 125.0)]
    public async Task The_randomness_spreads_the_wait_by_at_most_a_quarter(double jitter, double expectedMs)
    {
        var stub = new Stub((n, _, _) => Task.FromResult(n == 1 ? Reply(503) : Reply(200, Good)));
        var (client, waits) = Make(stub, jitter: jitter);
        await client.CompleteAsync("s", "u", CancellationToken.None);
        Assert.Equal(expectedMs, waits.Single().TotalMilliseconds, 3);
    }

    [Fact]
    public async Task No_wait_is_started_that_would_run_past_the_overall_deadline()
    {
        var stub = new Stub((_, _, _) => Task.FromResult(Reply(500)));
        var (client, waits) = Make(stub, delayMs: 5000, total: 1);
        var e = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal("server_error", e.Kind);
        Assert.Equal(1, stub.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task One_attempt_never_runs_longer_than_what_is_left_of_the_overall_deadline()
    {
        var stub = new Stub(async (_, _, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return Reply(200, Good); });
        var (client, _) = Make(stub, timeout: 30, total: 1);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("s", "u", CancellationToken.None));
        Assert.Equal("timeout", e.Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    // ---- the database: a command that hangs ends with an error

    [Fact]
    public void Every_database_command_has_a_time_limit()
    {
        Assert.Contains("Command Timeout=20", ConnectionString.Normalize("postgresql://u:p@db.example.com:5432/d"));
        Assert.Contains("Command Timeout=5", ConnectionString.Normalize("postgresql://u:p@db.example.com:5432/d", 5));
        Assert.Contains("Command Timeout=20", ConnectionString.Normalize("Host=h;Username=u;Password=p;Database=d"));
        Assert.Contains("Command Timeout=7", ConnectionString.Normalize("Host=h;Username=u;Password=p;Database=d;Command Timeout=7"));   // what a ready-made string says is kept
    }
}
