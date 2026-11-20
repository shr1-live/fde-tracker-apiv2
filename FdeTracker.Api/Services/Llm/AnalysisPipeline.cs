using System.Text.Json;
using FdeTracker.Api.Models.Requests;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// Runs one analysis for a prompt version and returns the answer text in the shape <see cref="AnalysisParser"/> reads.
/// Single-prompt versions are one model call. The chain (v7) is two: step 1 extracts quoted facts, step 2 rates from those facts only.
/// Between the steps our code keeps only the facts whose quote is really in the profile, so the second prompt never sees text the
/// first one invented, and text planted in a profile reaches the judge only if it is a real, quoted, non-instruction fact.
/// </summary>
public sealed class AnalysisPipeline(ILlmClient llm, PromptLibrary prompts)
{
    public async Task<LlmResult> Run(string version, string jobDescription, IReadOnlyList<CandidateInput> people, CancellationToken ct)
    {
        var message = PromptText.UserMessage(jobDescription, people);
        if (!PromptLibrary.IsChain(version)) return await llm.CompleteAsync(prompts.System(version), message, ct);

        var step1 = await llm.CompleteAsync(prompts.Extract, message, ct);
        var facts = CheckedFacts(step1.Text, people);
        var step2 = await llm.CompleteAsync(prompts.Judge, PromptText.FactsMessage(jobDescription, people, facts), ct);
        // One answer, with the cost of both steps added up.
        return new LlmResult(step2.Text, step1.InputTokens + step2.InputTokens, step1.OutputTokens + step2.OutputTokens, step1.LatencyMs + step2.LatencyMs, step2.Model);
    }

    /// <summary>Step 1's facts per candidate, with every fact dropped whose quote is not in that profile (instruction text does not count as the profile).</summary>
    internal static List<string> CheckedFacts(string raw, IReadOnlyList<CandidateInput> people)
    {
        using var doc = JsonText.Find(raw, "candidates") ?? throw new UnusableOutputException("The extraction step gave no JSON with a candidates list");
        var perCandidate = people.Select(_ => new { facts = new List<object>(), not_mentioned = new List<string>(), instructions_in_profile = false }).ToList();
        var byId = new Dictionary<int, (List<object> Facts, List<string> Missing, bool Flag)>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("candidates", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new UnusableOutputException("The extraction step gave no candidates list");
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var n) ? n : idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out var m) ? m : 0;
            if (id < 1 || id > people.Count || byId.ContainsKey(id)) continue;
            var profile = InstructionScreen.WithoutInstructions(people[id - 1].Profile);
            var facts = new List<object>();
            if (item.TryGetProperty("facts", out var f) && f.ValueKind == JsonValueKind.Array)
                foreach (var fact in f.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Take(20))
                {
                    var skill = Str(fact, "skill", 80); var evidence = Str(fact, "evidence", 300);
                    if (skill.Length > 0 && AnalysisParser.IsSupported(skill, evidence, profile)) facts.Add(new { skill, evidence });
                }
            var missing = item.TryGetProperty("not_mentioned", out var nm) && nm.ValueKind == JsonValueKind.Array
                ? nm.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => Clip(x.GetString() ?? "", 120)).Where(x => x.Length > 0).Take(15).ToList() : [];
            // The flag comes from our own screen as well as from the model, so a model that missed the order does not hide it from the judge.
            var flag = InstructionScreen.Find(people[id - 1].Profile).Count > 0 || InstructionScreen.Find(people[id - 1].Name).Count > 0
                || (item.TryGetProperty("instructions_in_profile", out var ip) && ip.ValueKind == JsonValueKind.True);
            byId[id] = (facts, missing, flag);
        }
        if (byId.Count == 0) throw new UnusableOutputException("The extraction step gave no usable candidate");
        return people.Select((p, i) =>
        {
            var (facts, missing, flag) = byId.TryGetValue(i + 1, out var v) ? v : ([], [], InstructionScreen.Find(p.Profile).Count > 0);
            return JsonSerializer.Serialize(new { facts, not_mentioned = missing, instructions_in_profile = flag });
        }).ToList();
    }

    static string Str(JsonElement o, string name, int max) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clip((v.GetString() ?? "").Trim(), max) : "";
    static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
