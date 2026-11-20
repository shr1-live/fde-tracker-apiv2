using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Api.Services.Lab;

/// <summary>One Day 1 test prompt and the simple checks its answer must pass (evals/day1-prompts.json).</summary>
public sealed class PromptCase
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("expect_any")] public string[]? ExpectAny { get; set; }
    [JsonPropertyName("expect_all")] public string[]? ExpectAll { get; set; }
    [JsonPropertyName("expect_none")] public string[]? ExpectNone { get; set; }
    [JsonPropertyName("expect_regex")] public string? ExpectRegex { get; set; }
    [JsonPropertyName("max_words")] public int? MaxWords { get; set; }
    [JsonPropertyName("min_question_marks")] public int? MinQuestionMarks { get; set; }
}

/// <summary>The Day 1 fixed prompt set: one system prompt and the cases.</summary>
public sealed class PromptSet
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("system")] public string System { get; set; } = "";
    [JsonPropertyName("cases")] public List<PromptCase> Cases { get; set; } = [];
}

/// <summary>
/// The Day 1 scoring rules. The evaluation tool (FdeTracker.Evals) and the web practice lab both call this, so a case
/// passes or fails the same way in both places.
/// </summary>
public static class Day1Scoring
{
    /// <summary>Finds the {"answer": ...} object even inside code fences or after a sentence, so no provider is marked down for its wrapping. Falls back to the whole text.</summary>
    public static string ExtractAnswer(string text)
    {
        using var doc = JsonText.Find(text, "answer");
        if (doc is not null && doc.RootElement.TryGetProperty("answer", out var a))
            return a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : a.ToString();
        return text;
    }

    /// <summary>Each case sets its own simple, repeatable checks.</summary>
    public static bool Score(PromptCase c, string answer)
    {
        var lower = answer.ToLowerInvariant();
        if (c.ExpectAny is { Length: > 0 } && !c.ExpectAny.Any(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectAll is { Length: > 0 } && !c.ExpectAll.All(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectNone is { Length: > 0 } && c.ExpectNone.Any(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectRegex is { } rx && !Regex.IsMatch(answer, rx, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return false;
        if (c.MaxWords is { } max && answer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > max) return false;
        if (c.MinQuestionMarks is { } q && answer.Count(ch => ch == '?') < q) return false;
        return true;
    }
}
