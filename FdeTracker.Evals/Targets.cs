using System.Text.Json;
using System.Text.Json.Serialization;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Evals;

/// <summary>One model to test. The API key is never stored here: only the NAME of the environment variable that holds it.</summary>
public sealed class Target
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("api_key_env")] public string? ApiKeyEnv { get; set; }
    /// <summary>"openai" (default) = any chat-completions provider; "anthropic" = Claude.</summary>
    [JsonPropertyName("provider")] public string Provider { get; set; } = "openai";
    /// <summary>false = skipped unless picked by name (use for paid models, so a key in your shell never spends money by accident).</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    /// <summary>Optional price in US dollars per million tokens, copied from the provider's price page. 0 for a model that runs on this computer. Left out when unknown: no cost is shown, never a guess.</summary>
    [JsonPropertyName("price_in_per_million")] public double? PriceInPerMillion { get; set; }
    [JsonPropertyName("price_out_per_million")] public double? PriceOutPerMillion { get; set; }
}

public sealed record ResolvedTarget(string Name, string BaseUrl, string Model, string ApiKey, string Provider);

public static class Targets
{
    /// <summary>Prices (input, output; dollars per million tokens) for the targets that have both set.</summary>
    public static Dictionary<string, (double In, double Out)> LoadPrices(string path)
    {
        try
        {
            var all = JsonSerializer.Deserialize<List<Target>>(File.ReadAllText(path)) ?? [];
            return all.Where(t => t.PriceInPerMillion is >= 0 && t.PriceOutPerMillion is >= 0).ToDictionary(t => t.Name, t => (t.PriceInPerMillion!.Value, t.PriceOutPerMillion!.Value), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Reads the targets file and looks up each key in the environment. A target with no key is skipped with a message,
    /// except local ones (localhost), which need none. Returns what can run.
    /// </summary>
    public static List<ResolvedTarget> Load(string path, string[]? only)
    {
        List<Target> all;
        try { all = JsonSerializer.Deserialize<List<Target>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read the targets file '{path}': {e.Message}");
            return [];
        }
        var run = new List<ResolvedTarget>();
        foreach (var t in all.Where(t => only is null || only.Contains(t.Name, StringComparer.OrdinalIgnoreCase)))
        {
            // Targets marked "enabled": false (for example a paid model) run only when picked by name with EVAL_ONLY.
            if (!t.Enabled && only is null)
            {
                Console.WriteLine($"Skipping {t.Name}: not enabled by default. Run it with EVAL_ONLY={t.Name}.");
                continue;
            }
            var provider = (t.Provider ?? "openai").ToLowerInvariant();
            if (!LlmClientFactory.Providers.Contains(provider))
            {
                Console.WriteLine($"Skipping {t.Name}: unknown provider '{t.Provider}'. Use one of: {string.Join(", ", LlmClientFactory.Providers)}.");
                continue;
            }
            // Only a local chat-completions server (for example Ollama) may run without a key. Claude always needs one.
            var local = provider == "openai" && IsLoopback(t.BaseUrl);
            var key = t.ApiKeyEnv is { Length: > 0 } ? Environment.GetEnvironmentVariable(t.ApiKeyEnv) : null;
            if (string.IsNullOrWhiteSpace(key) && !local)
            {
                Console.WriteLine($"Skipping {t.Name}: set the environment variable {t.ApiKeyEnv ?? "(api_key_env missing)"} to include it.");
                continue;
            }
            run.Add(new ResolvedTarget(t.Name, t.BaseUrl, t.Model, string.IsNullOrWhiteSpace(key) ? "local" : key, provider));
        }
        return run;
    }

    /// <summary>True only when the address really points at this computer (a parsed host, not a substring).</summary>
    static bool IsLoopback(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
