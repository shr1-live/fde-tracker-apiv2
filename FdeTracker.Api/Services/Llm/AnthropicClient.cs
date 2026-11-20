using System.Text;
using System.Text.Json;
using FdeTracker.Api.Common;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// Claude through Anthropic's Messages API. The key comes only from LLM_API_KEY (an environment variable).
/// The base address is optional and defaults to Anthropic's own.
/// </summary>
public sealed class AnthropicClient(HttpClient http, AppOptions options) : HttpLlmClient(http, options)
{
    const string DefaultBaseUrl = "https://api.anthropic.com";
    const string ApiVersion = "2023-06-01";

    public override bool IsConfigured => Options.LlmApiKey is not null && Options.LlmModel is not null;

    protected override HttpRequestMessage BuildRequest(string system, string user)
    {
        // No "JSON mode" switch here: the prompt asks for JSON and AnalysisParser tolerates extra words around it.
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Options.LlmModel,
            ["max_tokens"] = Options.LlmMaxTokens, // bounds the cost of one answer (LLM_MAX_TOKENS)
            ["temperature"] = Options.LlmTemperature,
            ["system"] = system,
            ["messages"] = new object[] { new { role = "user", content = user } },
        };
        if (Options.LlmTopP is { } topP) payload["top_p"] = topP;
        var body = JsonSerializer.Serialize(payload);
        // Always Anthropic's own address: LLM_BASE_URL belongs to the other provider type and is ignored here, so a stale
        // address left over from another provider can never receive the Claude key.
        var req = new HttpRequestMessage(HttpMethod.Post, DefaultBaseUrl + "/v1/messages") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("x-api-key", Options.LlmApiKey);
        req.Headers.Add("anthropic-version", ApiVersion);
        return req;
    }

    protected override LlmResult Parse(string json, int latencyMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var text = new StringBuilder();
            foreach (var block in root.GetProperty("content").EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var tx)) text.Append(tx.GetString());
            var stopReason = root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String ? stop.GetString() : null;
            if (stopReason == "refusal") throw LlmException.Blocked();
            if (stopReason == "max_tokens") throw new LlmException("truncated", "The answer was cut off at the length limit"); // a cut-off JSON answer cannot be used
            if (text.Length == 0) throw BadFormat(); // empty or refused: nothing usable
            return new LlmResult(text.ToString(), Usage(root, "input_tokens"), Usage(root, "output_tokens"), latencyMs, ModelOf(root));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw BadFormat(); }
    }
}

public static class LlmClientFactory
{
    public static readonly string[] Providers = ["openai", "anthropic"];

    /// <summary>"openai" means any provider with the common chat-completions API; "anthropic" means Claude.</summary>
    public static ILlmClient Create(string provider, HttpClient http, AppOptions options) => provider switch
    {
        "anthropic" => new AnthropicClient(http, options),
        "openai" => new OpenAiCompatibleClient(http, options),
        _ => throw new InvalidOperationException($"Unknown LLM provider '{provider}'. Use one of: {string.Join(", ", Providers)}"),
    };
}
