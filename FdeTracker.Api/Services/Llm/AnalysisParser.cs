using System.Text.Json;
using System.Text.RegularExpressions;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services.Llm;

public sealed class UnusableOutputException(string message) : Exception(message);

/// <summary>
/// Turns the model's text into checked data. Anything that is not in the agreed shape is refused, and every
/// "evidence" quote must really appear in that candidate's profile; claims without evidence are removed.
/// </summary>
public static partial class AnalysisParser
{
    static readonly string[] Fits = ["strong", "partial", "weak", "insufficient_information"];

    /// <param name="guardrails">Which checks run. Null means all of them, which is what the API uses; the evaluation tool passes smaller sets to measure what each check adds.</param>
    public static (List<CandidateAnalysisView> Candidates, List<string> Warnings) Parse(string raw, IReadOnlyList<CandidateInput> input, List<string> requiredSkills, GuardrailOptions? guardrails = null)
    {
        try { return ParseCore(raw, input, requiredSkills, guardrails ?? GuardrailOptions.All); }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            // A model can answer in any shape. Whatever it sends, the caller gets "unusable answer", never a crash.
            throw new UnusableOutputException("Unexpected shape (" + e.GetType().Name + ")"); // the type goes to the log, so a real bug is not mistaken for a model problem
        }
    }

    static (List<CandidateAnalysisView> Candidates, List<string> Warnings) ParseCore(string raw, IReadOnlyList<CandidateInput> input, List<string> requiredSkills, GuardrailOptions g)
    {
        // Some models add words or code fences around the JSON, so look for the object that holds the answer.
        var doc = JsonText.Find(raw, "candidates") ?? throw new UnusableOutputException("No JSON object with a candidates list");
        using var _ = doc;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("candidates", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new UnusableOutputException("Missing candidates list");

        var warnings = new List<string>();
        var byId = new Dictionary<int, CandidateAnalysisView>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idEl) || !TryReadId(idEl, out var id) || id < 1 || id > input.Count)
            {
                warnings.Add("An entry with an unknown candidate id was ignored.");
                continue;
            }
            if (byId.ContainsKey(id))
            {
                warnings.Add($"{input[id - 1].Name}: a second analysis for the same candidate was ignored.");
                continue;
            }
            byId[id] = ReadCandidate(item, id, input[id - 1], requiredSkills, warnings, g);
        }
        if (byId.Count == 0) throw new UnusableOutputException("No usable candidate in the answer");
        for (var i = 1; i <= input.Count; i++)
            if (!byId.ContainsKey(i)) warnings.Add($"{input[i - 1].Name}: the model returned no analysis for this candidate.");
        return (byId.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList(), warnings);
    }

    /// <summary>One candidate's analysis, with every quoted claim checked against that candidate's own profile.</summary>
    static CandidateAnalysisView ReadCandidate(JsonElement item, int id, CandidateInput person, List<string> requiredSkills, List<string> warnings, GuardrailOptions g)
    {
        // A profile that gives orders to the screener is flagged. What is left after removing those sentences is the only text
        // that may back a claim or count towards the skill check, so the injected text cannot vouch for itself.
        var instructions = g.ScreenInstructions ? [.. InstructionScreen.Find(person.Profile), .. InstructionScreen.Find(person.Name)] : new List<string>(); // the name reaches the prompt too
        var flagged = instructions.Count > 0;
        var profile = flagged ? InstructionScreen.WithoutInstructions(person.Profile) : person.Profile;
        if (flagged) warnings.Add($"{person.Name}: the profile contains instructions aimed at the screener (\"{instructions[0]}\"). They were ignored and the rating is limited.");

        var (matched, removed) = CheckClaims(item, profile, g.CheckQuotes);
        if (removed > 0) warnings.Add($"{person.Name}: {removed} claim(s) removed because the quoted evidence is not in the profile.");
        var fit = Text(item, "fit", 40);
        if (!Fits.Contains(fit)) fit = "insufficient_information";
        // A good rating needs at least one claim backed by a real quote, whatever the prompt says.
        if (g.LowerUnbackedRating && matched.Count == 0 && fit is "strong" or "partial")
        {
            fit = "insufficient_information";
            warnings.Add($"{person.Name}: rating lowered because no claim could be backed by a quote from the profile.");
        }
        var (found, missing, score) = SkillTools.ScoreMatch(requiredSkills, SkillTools.ExtractSkills(profile));
        // Our own skill check is independent of the model: "strong" needs at least half of the job's known skills in the profile.
        if (g.LimitRatingBySkillCheck && fit == "strong" && requiredSkills.Count > 0 && score < 50)
        {
            fit = "partial";
            warnings.Add($"{person.Name}: rating lowered because our own skill check found only {score}% of the job's skills in the profile.");
        }
        var notStated = g.CodeSummary ? [] : Strings(item, "not_stated");
        // The model's reasoning and questions are free text, like the summary: they are not shown for a profile that gave orders, nor when the summary is built in code.
        var reasoning = g.CodeSummary || flagged ? "" : Text(item, "reasoning", 400);
        var questions = g.CodeSummary ? CodeQuestions(fit, missing) : flagged ? [] : Strings(item, "questions").Take(3).Select(q => q.Length > 200 ? q[..200] : q).ToList();
        var summary = g.CodeSummary ? CodeSummary(fit, matched, missing) : Text(item, "summary", 600);
        if (flagged)
        {
            if (fit == "strong") fit = "partial";   // an instruction-carrying profile cannot earn the top rating
            if (!notStated.Contains("profile contains instructions")) notStated.Add("profile contains instructions");
            summary = "This profile contains instructions aimed at the screening assistant. They were ignored; the rating rests only on the facts the profile states.";
        }
        return new CandidateAnalysisView
        {
            Id = id, Name = person.Name,
            Summary = summary,
            MatchedSkills = matched,
            RemovedClaims = removed,
            Gaps = g.CodeSummary ? missing : flagged ? [] : Strings(item, "gaps"),   // the model's free text is not shown for a profile that gave orders, nor when the summary is built in code
            NotStated = notStated,
            Fit = fit,
            InstructionsFound = flagged,
            Reasoning = reasoning,
            ClarifyingQuestions = questions,
            Check = new SkillCheckView { Matched = found, Missing = missing, Score = score },
        };
    }

    /// <summary>
    /// A summary made only from things our code has checked: the rating, the skills that have a real quote, and the job's skills our own
    /// check did not find. No sentence of the model's is used, so text planted in a profile cannot reach the screen through it.
    /// </summary>
    static string CodeSummary(string fit, List<MatchedSkillView> matched, List<string> missing)
    {
        var rating = fit.Replace('_', ' ');
        var backed = matched.Count == 0 ? "No skill could be backed by a quote from the profile." : $"Skills backed by a quote from the profile: {string.Join(", ", matched.Select(m => m.Skill).Distinct())}.";
        var lacking = missing.Count == 0 ? "Every skill the job lists was found in the profile." : $"Skills the job lists that were not found in the profile: {string.Join(", ", missing)}.";
        return $"Rated {rating}. {backed} {lacking}";
    }

    /// <summary>Questions built by our code when the summary is: one per job skill our own check did not find (at most three), and a general one for a profile with nothing to judge.</summary>
    static List<string> CodeQuestions(string fit, List<string> missing)
    {
        if (fit == "strong") return [];
        var q = missing.Take(3).Select(s => $"Can you describe your experience with {s}?").ToList();
        if (q.Count == 0 && fit == "insufficient_information") q.Add("Can you share more detail about the work you have done?");
        return q;
    }

    static (List<MatchedSkillView> Kept, int Removed) CheckClaims(JsonElement item, string profile, bool checkQuotes)
    {
        var kept = new List<MatchedSkillView>();
        var removed = 0;
        foreach (var m in Array(item, "matched_skills"))
        {
            var skill = Text(m, "skill", 80);
            var evidence = Text(m, "evidence", 300);
            if (skill.Length == 0) continue;
            if (!checkQuotes || IsSupported(skill, evidence, profile)) kept.Add(new MatchedSkillView { Skill = skill, Evidence = evidence });
            else removed++;
        }
        return (kept, removed);
    }

    /// <summary>True when the quote is really in the profile and, for a known skill, mentions that skill. The evaluation tool uses this to count unsupported claims.</summary>
    public static bool IsSupported(string skill, string evidence, string profile) => IsQuoted(evidence, profile) && Supports(skill, evidence);

    /// <summary>The quote must be at least 8 characters and appear in the profile (ignoring case and spacing). The profile reaches the model with &lt; and &gt; escaped, so that spelling is accepted too.</summary>
    static bool IsQuoted(string evidence, string profile)
    {
        var e = Normalize(evidence);
        if (e.Length < 8) return false;
        return Normalize(profile).Contains(e, StringComparison.Ordinal) || Normalize(PromptText.Escape(profile)).Contains(e, StringComparison.Ordinal);
    }

    /// <summary>For skills we know, the quote must mention that skill ("Kubernetes" cannot be backed by a quote about Docker). Other skills only need a real quote.</summary>
    static bool Supports(string skill, string evidence) =>
        !SkillTools.IsKnownSkill(skill) || SkillTools.ExtractSkills(evidence).Contains(skill, StringComparer.OrdinalIgnoreCase);

    static string Normalize(string s) => Spaces().Replace(s, " ").Trim().ToLowerInvariant();
    /// <summary>The id as a number. Some models write it as text ("1"); that is accepted. Anything else is not an id.</summary>
    static bool TryReadId(JsonElement e, out int id)
    {
        id = 0;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetInt32(out id),
            JsonValueKind.String => int.TryParse(e.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id),
            _ => false,
        };
    }

    static IEnumerable<JsonElement> Array(JsonElement o, string name) =>
        o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(20) : [];

    static string Text(JsonElement o, string name, int max)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return "";
        var s = (v.GetString() ?? "").Trim();
        return s.Length > max ? s[..max] : s;
    }

    static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => (x.GetString() ?? "").Trim()).Where(x => x.Length > 0).Select(x => x.Length > 200 ? x[..200] : x).Take(15).ToList()
            : [];

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}

/// <summary>Candidate and job text is data. It is wrapped in tags and its angle brackets are escaped so it cannot close a tag or open a fake one.</summary>
public static class PromptText
{
    public static string Escape(string s) => s.Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>The user message for an analysis: the job and each candidate in tags, all text escaped.</summary>
    public static string UserMessage(string jobDescription, IReadOnlyList<CandidateInput> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<job_description>").AppendLine(Escape(jobDescription)).AppendLine("</job_description>");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var name = OneLine(Escape(c.Name)).Replace('"', '\'');
            sb.AppendLine($"<candidate id=\"{i + 1}\" name=\"{name}\">").AppendLine(Escape(c.Profile)).AppendLine("</candidate>");
        }
        return sb.ToString();
    }

    /// <summary>The step-2 message: the job, then each candidate's checked facts (as escaped JSON data). The profiles themselves are not sent again.</summary>
    public static string FactsMessage(string jobDescription, IReadOnlyList<CandidateInput> candidates, IReadOnlyList<string> factsJson)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<job_description>").AppendLine(Escape(jobDescription)).AppendLine("</job_description>");
        for (var i = 0; i < candidates.Count; i++)
        {
            var name = OneLine(Escape(candidates[i].Name)).Replace('"', '\'');
            sb.AppendLine($"<facts id=\"{i + 1}\" name=\"{name}\">").AppendLine(Escape(factsJson[i])).AppendLine("</facts>");
        }
        return sb.ToString();
    }

    static string OneLine(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
}
