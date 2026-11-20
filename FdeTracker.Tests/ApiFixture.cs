using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FdeTracker.Api.Services.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FdeTracker.Tests;

public sealed record TestUser(Guid Id, string Email);

/// <summary>A scripted model: each test sets what it answers and can read what it was sent.</summary>
public sealed class FakeLlm : ILlmClient
{
    public bool Configured { get; set; } = true;
    public bool IsConfigured => Configured;
    public string? Model => "fake-model";
    public Func<string, string, LlmResult>? Handler { get; set; }
    public string LastSystem { get; private set; } = "";
    public string LastUser { get; private set; } = "";
    public int Calls { get; private set; }

    /// <summary>How long the next answers take. The wait honours cancellation, like a real provider call.</summary>
    public TimeSpan Delay { get; set; }

    public async Task<LlmResult> CompleteAsync(string system, string user, CancellationToken ct)
    {
        LastSystem = system; LastUser = user; Calls++;
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        return (Handler ?? throw new InvalidOperationException("No fake answer set")).Invoke(system, user);
    }
}

public sealed record ApiResult(int Status, JsonNode? Json)
{
    public JsonNode Body => Json ?? throw new InvalidOperationException("No JSON body");
}

/// <summary>Starts a real Postgres in Docker and the API against it, once for the whole test class.</summary>
public class ApiFixture : IAsyncLifetime
{
    /// <summary>Every test class that starts the API joins this collection, so two APIs never start at once against each other's database.</summary>
    public const string Collection = "api";

    const string Secret = "test-secret-test-secret-test-secret-123456";
    const string Issuer = "https://test.supabase.co/auth/v1";
    readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    WebApplicationFactory<Program> _factory = null!;
    HttpClient _client = null!;

    public string ConnectionString => _pg.GetConnectionString();

    public static Guid Id(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}");

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Environment.SetEnvironmentVariable("DATABASE_URL", _pg.GetConnectionString());
        Environment.SetEnvironmentVariable("SUPABASE_URL", "https://test.supabase.co");
        Environment.SetEnvironmentVariable("SUPABASE_JWT_SECRET", Secret);
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "boss@fiftyfivetech.io");
        Environment.SetEnvironmentVariable("ALLOWED_DOMAIN", "fiftyfivetech.io");
        Environment.SetEnvironmentVariable("CORS_ORIGINS", "https://app.example");
        Environment.SetEnvironmentVariable("LLM_DAILY_CAP", "20");
        // The suite fires hundreds of requests in seconds; production keeps the defaults (240 per address, 40 writes per person).
        Environment.SetEnvironmentVariable("RATE_LIMIT_PER_MINUTE", "100000");
        Environment.SetEnvironmentVariable("TASK_MESSAGE_DEADLINE_SECONDS", "2"); // short, so the deadline test does not take a minute; the fake model answers at once
        Environment.SetEnvironmentVariable("WRITE_LIMIT_PER_MINUTE", "100000");
        Environment.SetEnvironmentVariable("REMINDERS_ENABLED", "false"); // no 10:00 timer in tests; they run reminders with a fixed clock
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ILlmClient>(); // tests never call a real model
            s.AddSingleton<ILlmClient>(BuildLlm);
        }));
        _client = _factory.CreateClient();
    }

    public FakeLlm Llm { get; } = new();

    /// <summary>The API's services, for tests that call a service directly (for example reminders with a fixed clock).</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>The model client the API gets. By default the scripted fake; a fixture may wrap it (for example to test the own-key switch).</summary>
    protected virtual ILlmClient BuildLlm(IServiceProvider services) => Llm;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _pg.DisposeAsync();
    }

    public static string Token(TestUser u, params Claim[] extra)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(Issuer, "authenticated", [new Claim("sub", u.Id.ToString()), new Claim("email", u.Email), .. extra], expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public async Task<ApiResult> Call(string method, string path, TestUser? user = null, object? body = null, string? rawToken = null, Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        foreach (var (name, value) in headers ?? []) req.Headers.TryAddWithoutValidation(name, value);
        if (user is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        else if (rawToken is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        if (body is not null) req.Content = new StringContent(body is string s ? s : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await _client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? json = null;
        try { json = string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text); } catch (JsonException) { /* not json */ }
        return new ApiResult((int)res.StatusCode, json);
    }

    public Task<HttpResponseMessage> Raw(string path) => _client.GetAsync(path);

    /// <summary>Uploads one file as a multipart form, like the browser does. The type we claim is deliberately useless: the server must not trust it.</summary>
    public async Task<ApiResult> Upload(string path, TestUser user, string fileName, byte[] bytes, string? label = null)
    {
        using var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", fileName);
        if (label is not null) form.Add(new StringContent(label), "label");
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        using var res = await _client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? json = null;
        try { json = string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text); } catch (JsonException) { /* not json */ }
        return new ApiResult((int)res.StatusCode, json);
    }

    public sealed record FileResult(int Status, string? ContentType, string? Disposition, string? NoSniff, string? Csp, byte[] Body);

    public async Task<FileResult> Download(string path, TestUser? user)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user));
        using var res = await _client.SendAsync(req);
        string? H(string name) => res.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
        return new FileResult((int)res.StatusCode, res.Content.Headers.ContentType?.ToString(), res.Content.Headers.ContentDisposition?.ToString(), H("X-Content-Type-Options"), H("Content-Security-Policy"), await res.Content.ReadAsByteArrayAsync());
    }

    public Task<HttpResponseMessage> GetFromOrigin(string path, string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Origin", origin);
        return _client.SendAsync(req);
    }

    /// <summary>Sign-in is invite-only: stores open invites (as members of the seeded FDE program) so these people are approved when they first sign in.</summary>
    public async Task Invite(params TestUser[] users)
    {
        Assert.Equal(200, (await Call("GET", "/health")).Status); // the API (and its migrations) has started
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var u in users)
        {
            await using var cmd = new NpgsqlCommand(
                "insert into lp_invites (email, program_id, role) select @email, id, 'member' from lp_programs where name = 'FDE Training Program' on conflict (email, program_id) do nothing", conn);
            cmd.Parameters.AddWithValue("email", u.Email.ToLowerInvariant());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<long> Count(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}

/// <summary>The scenarios share one database and build on each other, so they run in name order (T01, T02, ...).</summary>
public sealed class NameOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase =>
        testCases.OrderBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);
}
