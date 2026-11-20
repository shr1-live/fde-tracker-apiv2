using System.Text.Json;
using System.Text.RegularExpressions;
using FdeTracker.Api.Common;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// A person's own Gemini key for ONE request. It lives only in memory for the length of that request: it is never written to the database,
/// never logged, and never put in a response. The address it is sent to is fixed in code (<see cref="GeminiKeyClient.Endpoint"/>), so a pasted key
/// cannot make the server call anywhere else.
/// </summary>
public sealed record OwnKey(string ApiKey, string? Model);

/// <summary>Holds the current request's own key so the model client can pick it up without every method passing it along.</summary>
public static class LlmKeyScope
{
    static readonly AsyncLocal<OwnKey?> Slot = new();
    /// <summary>Calls with their own key are not charged to the shared daily limit; this only stops one person hammering the server.</summary>
    public const int OwnKeyDailyCap = 500;

    public static OwnKey? Current => Slot.Value;

    /// <summary>The daily limit that applies to the current request.</summary>
    public static int CapFor(AppOptions options) => Current is null ? options.LlmDailyCap : OwnKeyDailyCap;

    public static IDisposable Use(OwnKey key)
    {
        Slot.Value = key;
        return new Reset();
    }

    sealed class Reset : IDisposable { public void Dispose() => Slot.Value = null; }
}

/// <summary>Google's Gemini through its OpenAI-compatible address, using the key and model the person pasted.</summary>
public sealed class GeminiKeyClient(HttpClient http, AppOptions options, OwnKey key) : OpenAiCompatibleClient(http, options)
{
    public const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai";
    protected override string? ApiKey => key.ApiKey;
    protected override string? BaseUrl => Endpoint;
    protected override string? ModelName => key.Model;
}

/// <summary>
/// The one model client the app uses. For a request that carries its own key it talks to Gemini with that key;
/// for every other request it is exactly the server's configured client.
/// </summary>
public sealed class SwitchingLlmClient(ILlmClient server, HttpClient http, AppOptions options) : ILlmClient
{
    ILlmClient Active => LlmKeyScope.Current is { Model: not null } k ? new GeminiKeyClient(http, options, k) : server;
    public bool IsConfigured => LlmKeyScope.Current is { Model: not null } || server.IsConfigured;
    public string? Model => Active.Model;
    public Task<LlmResult> CompleteAsync(string system, string user, CancellationToken ct) => Active.CompleteAsync(system, user, ct);
}

public sealed record GeminiModels(List<string> Models, string? Suggested);

/// <summary>Asks Gemini which models a key can use, so a person picks a real one instead of typing a name that may be retired.</summary>
public static class GeminiCatalog
{
    static readonly string[] Unwanted = ["embedding", "tts", "image", "imagen", "live", "audio", "robotics", "computer-use", "aqa", "learnlm", "veo"];
    static readonly Regex Plain = new(@"^gemini-(\d+(?:\.\d+)?)-flash$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    public static async Task<GeminiModels> ListAsync(HttpClient http, string apiKey, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GeminiKeyClient.Endpoint + "/models");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var res = await http.SendAsync(req, timeout.Token);
            var text = await res.Content.ReadAsStringAsync(timeout.Token);
            if (!res.IsSuccessStatusCode) throw LlmException.FromStatus((int)res.StatusCode);
            return Parse(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw LlmException.Timeout(15); }
        catch (HttpRequestException) { throw LlmException.Network(); }
    }

    public static GeminiModels Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var ids = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "")
                .Where(id => id.Length > 0).Select(id => id.StartsWith("models/", StringComparison.Ordinal) ? id[7..] : id);
            var usable = Usable(ids);
            return new GeminiModels(usable, Suggest(usable));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw LlmException.BadFormat(); }
    }

    /// <summary>Only text models people chat with: no embeddings, images, speech, live or other special-purpose ones.</summary>
    public static List<string> Usable(IEnumerable<string> ids) =>
        ids.Where(id => id.StartsWith("gemini-", StringComparison.Ordinal) && !Unwanted.Any(u => id.Contains(u, StringComparison.Ordinal)))
           .Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>The newest plain "flash" model (fast and cheap enough for a free key); else the first one.</summary>
    public static string? Suggest(List<string> usable)
    {
        string? best = null; var bestVersion = -1.0;
        foreach (var id in usable)
        {
            var m = Plain.Match(id);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > bestVersion) { best = id; bestVersion = v; }
        }
        return best ?? usable.FirstOrDefault();
    }
}
