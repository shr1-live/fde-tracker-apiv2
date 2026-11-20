using System.Text.Json;
using System.Text.RegularExpressions;

namespace FdeTracker.Api.Services.Llm;

/// <summary>Finds the JSON object a model meant to send, even when it adds code fences or sentences (that may contain braces) around it.</summary>
public static partial class JsonText
{
    /// <summary>
    /// Returns the first JSON object that has the named property: the whole text if it fits, otherwise the first balanced
    /// { ... } block that parses and has it (braces inside strings are ignored). The caller disposes the result. Null when none.
    /// </summary>
    public static JsonDocument? Find(string raw, string requiredProperty)
    {
        var text = Fence().Replace(raw.Trim(), "").Trim();
        if (TryParse(text) is { } whole)
        {
            if (Has(whole, requiredProperty)) return whole;
            whole.Dispose();
        }
        for (var start = text.IndexOf('{'); start >= 0; start = text.IndexOf('{', start + 1))
        {
            var end = BlockEnd(text, start);
            if (end < 0) continue;
            var doc = TryParse(text[start..(end + 1)]);
            if (doc is not null && Has(doc, requiredProperty)) return doc;
            doc?.Dispose();
        }
        return null;
    }

    /// <summary>Index of the brace that closes the block opening at <paramref name="start"/>, or -1.</summary>
    static int BlockEnd(string t, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < t.Length; i++)
        {
            var ch = t[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
            }
            else if (ch == '"') inString = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return i;
        }
        return -1;
    }

    static JsonDocument? TryParse(string s)
    {
        try { return JsonDocument.Parse(s); }
        catch (JsonException) { return null; }
    }

    static bool Has(JsonDocument d, string property) => d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty(property, out _);

    [GeneratedRegex(@"^```[a-zA-Z]*\s*|\s*```\z")] private static partial Regex Fence();
}
