using System.Text.Json.Serialization;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services.Lab;

public sealed class Day2Expect
{
    [JsonPropertyName("fit")] public string[] Fit { get; set; } = [];
    [JsonPropertyName("must_not_claim")] public string[] MustNotClaim { get; set; } = [];
    [JsonPropertyName("incomplete")] public bool Incomplete { get; set; }
    [JsonPropertyName("injection")] public bool Injection { get; set; }
}

public sealed class Day2Candidate
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    [JsonPropertyName("expect")] public Day2Expect Expect { get; set; } = new();
}

/// <summary>The Day 2 fixed job description and candidates (evals/day2-cases.json).</summary>
public sealed class Day2Cases
{
    [JsonPropertyName("job_description")] public string JobDescription { get; set; } = "";
    [JsonPropertyName("candidates")] public List<Day2Candidate> Candidates { get; set; } = [];
}

/// <summary>Which Day 2 checks one candidate's analysis passed.</summary>
public sealed record Day2Check(string Name, bool Present, bool Grounded, bool FitOk, bool NoForbidden, bool IncompleteOk, string Fit, string[] Skills, bool Clarifies, bool HasReasoning)
{
    /// <summary>All five checks the Day 2 report counts: usable, grounded, sensible fit, no invented skills, missing information shown.</summary>
    public bool Passed => Present && Grounded && FitOk && NoForbidden && IncompleteOk;
}

/// <summary>
/// The Day 2 scoring rules, shared by the evaluation tool (FdeTracker.Evals) and the web practice lab.
/// Input: the candidates sent in one call (in order) and the parsed, checked analyses (ids 1..n).
/// </summary>
public static class Day2Scoring
{
    public static List<Day2Check> Score(IReadOnlyList<Day2Candidate> group, IReadOnlyList<CandidateAnalysisView> views) =>
        group.Select((c, i) =>
        {
            var v = views.FirstOrDefault(x => x.Id == i + 1);
            var present = v is not null;
            var skills = v?.MatchedSkills.Select(m => m.Skill).ToArray() ?? [];
            var grounded = present && v!.RemovedClaims == 0; // structured count from the parser, not text matching
            var fitOk = present && c.Expect.Fit.Contains(v!.Fit);
            var clean = present && !skills.Any(s => c.Expect.MustNotClaim.Contains(s, StringComparer.OrdinalIgnoreCase));
            var incompleteOk = !c.Expect.Incomplete || (present && (v!.Fit == "insufficient_information" || v.NotStated.Count > 0));
            return new Day2Check(c.Name, present, grounded, fitOk, clean, incompleteOk, v?.Fit ?? "", skills, v?.ClarifyingQuestions.Count > 0, v?.Reasoning.Length > 0);
        }).ToList();

    /// <summary>A short plain-English line saying which checks failed (or that all passed).</summary>
    public static string Note(Day2Candidate c, Day2Check r)
    {
        if (!r.Present) return "The model gave no analysis for this candidate.";
        var problems = new List<string>();
        if (!r.FitOk) problems.Add($"Rated \"{r.Fit}\"; expected {string.Join(" or ", c.Expect.Fit.Select(f => "\"" + f + "\""))}.");
        if (!r.Grounded) problems.Add("Some claimed skills had no real quote from the profile and were removed.");
        var invented = r.Skills.Where(s => c.Expect.MustNotClaim.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        if (invented.Count > 0) problems.Add("Claimed skills the profile does not support: " + string.Join(", ", invented) + ".");
        if (!r.IncompleteOk) problems.Add("Did not say that information is missing from this thin profile.");
        return problems.Count == 0 ? "All checks passed." : string.Join(" ", problems);
    }
}
