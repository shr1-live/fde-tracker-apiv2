using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// Which checks run on the model's answer. Production always uses <see cref="All"/>. The evaluation tool also runs the
/// smaller sets on the same raw answers, so "before" and "after" differ only by the checks.
/// </summary>
/// <param name="CodeSummary">Show a summary, gaps and not-stated list built in code from the checked fields, instead of the model's own free text. Free text can carry words an attacker planted, and code cannot judge free text.</param>
public sealed record GuardrailOptions(bool CheckQuotes, bool LowerUnbackedRating, bool ScreenInstructions, bool LimitRatingBySkillCheck, bool CodeSummary = false)
{
    /// <summary>Everything on. This is what the API uses.</summary>
    public static readonly GuardrailOptions All = new(true, true, true, true);
    /// <summary>Everything on, and the summary written by our code (ASSISTANT_SUMMARY=code). Safer against text planted in a profile; the wording is plainer.</summary>
    public static readonly GuardrailOptions AllWithCodeSummary = new(true, true, true, true, true);
    /// <summary>What existed before Day 4: quotes must be real, and a rating needs one real quote.</summary>
    public static readonly GuardrailOptions Existing = new(true, true, false, false);
    /// <summary>No checks: the model's answer as it came (still has to parse).</summary>
    public static readonly GuardrailOptions None = new(false, false, false, false);
}

/// <summary>
/// Finds text in a profile (or a name) that tries to give orders to the screening assistant. A profile is data, so an order
/// in it is suspicious by itself: a real profile describes a person, it does not address the reader's system.
/// The text is normalised first (compatibility forms, hidden format characters, common look-alike letters, any whitespace),
/// so "ig​nore" or a line break in the middle of a phrase does not hide it. This is a screen, not a wall: the hard limits
/// (a rating needs a real quote, "strong" needs our own skill check) hold even when the screen is fooled.
/// </summary>
public static partial class InstructionScreen
{
    // Words an attacker might want in front of an order, written as letters from other alphabets that look like Latin ones.
    static readonly Dictionary<char, char> LookAlikes = new()
    {
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['х'] = 'x', ['у'] = 'y', ['і'] = 'i', ['ј'] = 'j', ['ѕ'] = 's', ['ԁ'] = 'd', ['ɡ'] = 'g',
        ['α'] = 'a', ['ο'] = 'o', ['ρ'] = 'p', ['ν'] = 'v', ['ι'] = 'i', ['κ'] = 'k', ['τ'] = 't',
    };

    // After normalising, every gap is a single space, and gaps between words never cross a sentence end.
    static readonly Regex[] Patterns =
    [
        New(@"\b(ignore|disregard)\s+(?:(?:all|any|the|your|my|these|those|every|previous|prior|above|earlier|preceding|other)\s+)*(instructions?|rules?|prompts?|guidelines|directions|job description)\b"),
        New(@"\b(system|developer|admin)\s+(note|message|override|instruction)s?\b[^.!?:]{0,30}:"),
        New(@"\bnote to (the )?(assistant|ai|model|screener|reviewer)\b"),
        New(@"\byou are now\b"),
        New(@"\brate (me|this candidate|him|her|them)\b[^.!?]{0,20}\b(strong|excellent|top|best)\b"),
        New(@"\b(print|reveal|show|repeat|leak)\b[^.!?]{0,25}\b(system prompt|your prompt|your instructions|these rules|the rules)\b"),
        New(@"\b(reply|respond|answer|output)\s+only\s+with\b|\b(reply|respond|answer)\s+with\s+(the\s+)?(word|text|phrase|string)\b"),
        New(@"\bexactly and nothing else\b"),
        New(@"</?\s*(candidate|job_description|tool_result|request)\b"),
    ];

    static Regex New(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    /// <summary>The text as the patterns see it: NFKC, no hidden format characters, look-alikes folded, one space between words.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format) continue;      // zero-width joiner, soft hyphen, direction marks...
            var folded = LookAlikes.TryGetValue(char.ToLowerInvariant(ch), out var latin) ? latin : ch;
            sb.Append(char.IsWhiteSpace(folded) ? ' ' : folded);
        }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Short excerpts of what matched (at most three), or an empty list for ordinary text.</summary>
    public static List<string> Find(string text)
    {
        var hits = new List<string>();
        var seen = Normalize(text);
        foreach (var p in Patterns)
        {
            try
            {
                var m = p.Match(seen);
                if (m.Success) hits.Add(Clip(m.Value));
            }
            catch (RegexMatchTimeoutException) { hits.Add("(unreadable text)"); } // text built to stall the matcher is treated as suspicious
            if (hits.Count == 3) break;
        }
        return hits;
    }

    /// <summary>
    /// The profile without the passages that contain an order, so the quote check and the skill check cannot be satisfied by the
    /// injected text itself. Passages are sentences and paragraphs; a line break inside a sentence does not split it.
    /// </summary>
    public static string WithoutInstructions(string profile)
    {
        var kept = Passages().Split(profile).Where(s => s.Length > 0 && Find(s).Count == 0);
        return string.Join(" ", kept).Trim();
    }

    static string Clip(string s) => new(s.Take(40).Where(c => !char.IsControl(c)).ToArray());

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n\s*\r?\n", RegexOptions.None, 500)] private static partial Regex Passages();
}
