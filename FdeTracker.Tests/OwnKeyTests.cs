using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FdeTracker.Api.Common;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Tests;

/// <summary>A person's own Gemini key: where it is sent, that it never leaks or crosses to another request, and how errors are worded. No database, no network.</summary>
public sealed class OwnKeyTests
{
    const string OwnKey = "AIzaSyOWNKEY_0123456789abcdefghijklmnop";
    const string OtherKey = "AIzaSyOTHER_0123456789abcdefghijklmnopq";
    const string Answer = """{"model":"gemini-test","choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""";

    sealed class Stub(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            await reply(request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
    }

    static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    static AppOptions Opts() => new() { LlmProvider = "openai", LlmApiKey = "server-key", LlmModel = "server-model", LlmBaseUrl = "https://server.example/v1", LlmRetryDelayMs = 1, LlmTimeoutSeconds = 5 };

    [Fact]
    public async Task An_own_key_goes_to_googles_fixed_address_and_the_servers_key_is_not_used()
    {
        var seen = new List<(Uri? Uri, string? Auth, string Body)>();
        var http = new HttpClient(new Stub((req, body) => { seen.Add((req.RequestUri, req.Headers.Authorization?.ToString(), body)); return Task.FromResult(Json(HttpStatusCode.OK, Answer)); }));
        var options = Opts();
        var client = new SwitchingLlmClient(LlmClientFactory.Create("openai", http, options), http, options);

        using (LlmKeyScope.Use(new OwnKey(OwnKey, "gemini-test")))
        {
            Assert.True(client.IsConfigured);
            Assert.Equal("gemini-test", client.Model);
            var r = await client.CompleteAsync("sys", "user", CancellationToken.None);
            Assert.Equal("gemini-test", r.Model);
        }
        var own = Assert.Single(seen);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", own.Uri!.ToString());
        Assert.Equal("Bearer " + OwnKey, own.Auth);
        Assert.Equal("gemini-test", (string)JsonNode.Parse(own.Body)!["model"]!);
        Assert.DoesNotContain("server-key", own.Auth);
        Assert.DoesNotContain("server.example", own.Uri.ToString());

        // after the request, the very same client is the server's client again
        await client.CompleteAsync("sys", "user", CancellationToken.None);
        Assert.Equal("https://server.example/v1/chat/completions", seen[1].Uri!.ToString());
        Assert.Equal("Bearer server-key", seen[1].Auth);
        Assert.Null(LlmKeyScope.Current);
    }

    [Fact]
    public async Task Two_requests_at_the_same_time_never_share_a_key()
    {
        var used = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var http = new HttpClient(new Stub(async (req, body) =>
        {
            await Task.Delay(50); // let both requests be in flight together
            used[(string)JsonNode.Parse(body)!["messages"]![1]!["content"]!] = req.Headers.Authorization!.Parameter!;
            return Json(HttpStatusCode.OK, Answer);
        }));
        var options = Opts();
        var client = new SwitchingLlmClient(LlmClientFactory.Create("openai", http, options), http, options);

        async Task Run(string who, string key)
        {
            using (LlmKeyScope.Use(new OwnKey(key, "gemini-test"))) { await Task.Delay(10); await client.CompleteAsync("sys", who, CancellationToken.None); }
        }
        await Task.WhenAll(Task.Run(() => Run("ann", OwnKey)), Task.Run(() => Run("bob", OtherKey)));

        Assert.Equal(OwnKey, used["ann"]);
        Assert.Equal(OtherKey, used["bob"]);
    }

    [Fact]
    public void Own_key_calls_use_their_own_daily_limit_not_the_shared_one()
    {
        var options = new AppOptions { LlmDailyCap = 20 };
        Assert.Equal(20, LlmKeyScope.CapFor(options));
        using (LlmKeyScope.Use(new OwnKey(OwnKey, "gemini-test"))) Assert.Equal(LlmKeyScope.OwnKeyDailyCap, LlmKeyScope.CapFor(options));
        Assert.Equal(20, LlmKeyScope.CapFor(options));
    }

    [Fact]
    public void A_refused_own_key_is_the_persons_to_fix_and_the_message_never_holds_the_key()
    {
        var auth = LlmFailure.ToApi(LlmException.FromStatus(403), ownKey: true);
        Assert.Equal(400, auth.Status);
        Assert.Contains("Gemini refused your key", auth.Message);
        Assert.DoesNotContain("administrator", auth.Message);
        Assert.DoesNotContain("AIza", auth.Message);
        Assert.Contains("administrator", LlmFailure.ToApi(LlmException.FromStatus(403)).Message); // the shared key is still the administrator's job

        Assert.Contains("Test key", LlmFailure.ToApi(LlmException.FromStatus(404), ownKey: true).Message);
        var limited = LlmFailure.ToApi(LlmException.FromStatus(429, TimeSpan.FromSeconds(12)), ownKey: true);
        Assert.Equal(429, limited.Status);
        Assert.Contains("your Gemini key", limited.Message);
        Assert.Contains("12", limited.Message);
    }

    [Fact]
    public void The_model_list_keeps_chat_models_and_suggests_the_newest_plain_flash()
    {
        var json = """
            {"object":"list","data":[
              {"id":"models/gemini-2.0-flash"},{"id":"models/gemini-2.5-flash"},{"id":"models/gemini-2.5-flash-lite"},{"id":"models/gemini-2.5-pro"},
              {"id":"models/gemini-2.5-flash-preview-tts"},{"id":"models/gemini-embedding-001"},{"id":"models/gemini-2.5-flash-image"},
              {"id":"models/imagen-4.0-generate"},{"id":"models/gemma-3-27b-it"},{"id":"models/gemini-2.0-flash-live-001"}]}
            """;
        var m = GeminiCatalog.Parse(json);
        Assert.Equal(["gemini-2.0-flash", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro"], m.Models);
        Assert.Equal("gemini-2.5-flash", m.Suggested);
        Assert.Null(GeminiCatalog.Suggest([]));
        Assert.Equal("gemini-2.5-pro", GeminiCatalog.Suggest(["gemini-2.5-pro"])); // no plain flash: the first one
    }

    [Fact]
    public async Task Listing_models_sends_the_key_as_a_bearer_header_never_in_the_address_and_maps_a_refusal()
    {
        Uri? uri = null; string? auth = null;
        var ok = new HttpClient(new Stub((req, _) => { uri = req.RequestUri; auth = req.Headers.Authorization?.ToString(); return Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"id":"models/gemini-2.5-flash"}]}""")); }));
        var list = await GeminiCatalog.ListAsync(ok, OwnKey, CancellationToken.None);
        Assert.Equal("gemini-2.5-flash", list.Suggested);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/models", uri!.ToString());
        Assert.DoesNotContain("AIza", uri.ToString());
        Assert.Equal("Bearer " + OwnKey, auth);

        var refused = new HttpClient(new Stub((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid AIzaSyOWNKEY"}}"""))));
        var e = await Assert.ThrowsAsync<LlmException>(() => GeminiCatalog.ListAsync(refused, OwnKey, CancellationToken.None));
        Assert.DoesNotContain("AIza", e.Message); // Google's own text (which may repeat the key) is never passed on
    }
}

/// <summary>The header on the real request pipeline: who may send it, which routes read it, and that it is never echoed.</summary>
[Collection(ApiFixture.Collection)]
public sealed class OwnKeyApiTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    // Sign-in is invite-only: these people are invited (as members of the seeded program) before they first sign in.
    public Task InitializeAsync() => api.Invite(Learner);
    public Task DisposeAsync() => Task.CompletedTask;

    const string Key = "AIzaSyOWNKEY_0123456789abcdefghijklmnop";
    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser Learner = new(ApiFixture.Id(2), "b@fiftyfivetech.io");

    static Dictionary<string, string> With(string key = Key, string? model = "gemini-2.5-flash") =>
        model is null ? new() { ["X-Gemini-Key"] = key } : new() { ["X-Gemini-Key"] = key, ["X-Gemini-Model"] = model };

    static object Req() => new
    {
        job_description = "We need a backend engineer with Python and Docker experience. Knowledge of PostgreSQL is a plus.",
        candidates = new[] { new { name = "Candidate 1", profile = "Built REST APIs in Python for three years. Deployed services with Docker on AWS." } },
    };

    [Fact]
    public async Task A_key_header_without_sign_in_is_refused_like_any_other_request()
    {
        Assert.Equal(401, (await api.Call("GET", "/api/assistant/usage", headers: With())).Status);
    }

    [Fact]
    public async Task A_badly_shaped_key_or_model_is_refused_and_never_repeated_in_the_answer()
    {
        await api.Call("GET", "/api/me", Admin);
        foreach (var bad in new[] { "not-a-key", "AIzaX", "AIzashort", "sk-1234567890123456789012345678901234567890", new string('A', 400) })
        {
            var r = await api.Call("GET", "/api/assistant/usage", Admin, headers: With(bad));
            Assert.Equal(400, r.Status);
            Assert.DoesNotContain(bad, r.Json!.ToJsonString());
        }
        var model = await api.Call("GET", "/api/assistant/usage", Admin, headers: With(Key, "../../etc/passwd"));
        Assert.Equal(400, model.Status);
        Assert.DoesNotContain("passwd", model.Json!.ToJsonString());
    }

    [Fact]
    public async Task Running_an_analysis_with_a_key_needs_a_chosen_model()
    {
        await api.Call("GET", "/api/me", Admin);
        var r = await api.Call("POST", "/api/assistant/analyze", Admin, Req(), headers: With(Key, model: null));
        Assert.Equal(400, r.Status);
        Assert.Contains("model", (string)r.Body["error"]!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AIza", r.Json!.ToJsonString());
    }

    [Fact]
    public async Task The_key_is_read_on_the_assistant_routes_only()
    {
        await api.Call("GET", "/api/me", Admin);
        // a garbage header on any other route is simply ignored
        Assert.Equal(200, (await api.Call("GET", "/api/me", Admin, headers: With("garbage"))).Status);
        var usage = await api.Call("GET", "/api/assistant/usage", Admin, headers: With());
        Assert.Equal(200, usage.Status);
        Assert.True((bool)usage.Body["using_own_key"]!);
        Assert.False((bool)(await api.Call("GET", "/api/assistant/usage", Admin)).Body["using_own_key"]!);
    }

    [Fact]
    public async Task The_newer_AQ_key_format_is_accepted_too()
    {
        await api.Call("GET", "/api/me", Admin);
        var r = await api.Call("GET", "/api/assistant/usage", Admin, headers: With("AQ.Ab8RN6Lw_example-key.part_0123456789abcdef"));
        Assert.Equal(200, r.Status);
        Assert.True((bool)r.Body["using_own_key"]!);
        Assert.Equal(400, (await api.Call("GET", "/api/assistant/usage", Admin, headers: With("AQ.short"))).Status);
        Assert.Equal(400, (await api.Call("GET", "/api/assistant/usage", Admin, headers: With("AQ.has space in it 0123456789"))).Status);
    }

    [Fact]
    public async Task The_model_list_needs_a_key()
    {
        await api.Call("GET", "/api/me", Admin);
        Assert.Equal(400, (await api.Call("GET", "/api/assistant/models", Admin)).Status);
    }

    [Fact]
    public async Task Calls_with_an_own_key_are_not_charged_to_the_shared_daily_limit()
    {
        await api.Call("GET", "/api/me", Admin);
        await api.Call("GET", "/api/me", Learner);
        api.Llm.Handler = (_, _) => new LlmResult("""{"candidates":[{"id":1,"summary":"ok","fit":"weak","matched_skills":[],"gaps":[],"not_stated":[]}]}""", 10, 5, 100, "fake-model");
        for (var i = 0; i < 22; i++) Assert.Equal(200, (await api.Call("POST", "/api/assistant/analyze", Learner, Req(), headers: With())).Status);
        var usage = (await api.Call("GET", "/api/assistant/usage", Learner, headers: With())).Body;
        Assert.Equal(LlmKeyScope.OwnKeyDailyCap, (int)usage["daily_cap"]!);
        // own-key calls are counted apart, so the shared limit (20) is untouched by those 22 calls: the server model still answers
        Assert.Equal(0, (int)(await api.Call("GET", "/api/assistant/usage", Learner)).Body["used_today"]!);
        Assert.Equal(200, (await api.Call("POST", "/api/assistant/analyze", Learner, Req())).Status);
    }
}
