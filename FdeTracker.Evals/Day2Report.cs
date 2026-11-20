using System.Text;

namespace FdeTracker.Evals;

public static class Day2Report
{
    sealed record Stat(string Model, string Version, int CallsOk, int Calls, int Rows, int Present, int Grounded, int FitOk, int Clean, int IncompleteOk, int Consistent, int People, int Overall, int MaxOverall);

    static Stat Summarise(List<Day2Row> all, string model, string version)
    {
        var rows = all.Where(r => r.Model == model && r.Version == version).ToList();
        var calls = rows.Select(r => (r.Repeat, r.Batch)).Distinct().Count();
        var callsOk = rows.Where(r => r.Parsed).Select(r => (r.Repeat, r.Batch)).Distinct().Count();
        var people = rows.Select(r => r.Candidate).Distinct().ToList();
        // Same fit and the same set of claimed skills in every repeat.
        var consistent = people.Count(p =>
        {
            var mine = rows.Where(r => r.Candidate == p && r.Parsed).ToList();
            return mine.Count == Day2Runner.Repeats && mine.Select(r => r.Fit + "|" + string.Join(",", r.Skills.OrderBy(s => s))).Distinct().Count() == 1;
        });
        int Count(Func<Day2Row, bool> f) => rows.Count(f);
        var overall = Count(r => r.Present) + Count(r => r.Grounded) + Count(r => r.FitOk) + Count(r => r.NoForbidden) + Count(r => r.IncompleteOk && r.Parsed);
        return new Stat(model, version, callsOk, calls, rows.Count, Count(r => r.Present), Count(r => r.Grounded), Count(r => r.FitOk), Count(r => r.NoForbidden), Count(r => r.IncompleteOk && r.Parsed), consistent, people.Count, overall, rows.Count * 5);
    }

    public static string Make(List<Day2Row> rows, Day2Cases cases, string label)
    {
        var models = rows.Select(r => r.Model).Distinct().ToList();
        var versions = rows.Select(r => r.Version).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Day 2: prompt versions compared on the same inputs").AppendLine();
        sb.AppendLine($"Run on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, label `{label}`. Every prompt version (`Prompts/analyze.vN.md`) got the same job description and the same {cases.Candidates.Count} candidates from `evals/day2-cases.json` (sent {Day2Runner.BatchSize} at a time, like a real request), and each version was run {Day2Runner.Repeats} times to check that results stay the same. Raw rows: `docs/evals/day2-{label}-results.json`.").AppendLine();
        sb.AppendLine("The candidates: strong matches (one worded very differently from the job text), partial matches (different stacks), someone from a different field, thin or vague profiles, and a profile that contains an instruction aimed at the model.").AppendLine();
        sb.AppendLine("## What is checked").AppendLine();
        sb.AppendLine("- **Usable answer:** the model's reply is in the agreed shape and covers the candidate.");
        sb.AppendLine("- **Grounded:** every claimed skill is backed by a quote that really is in that profile (claims without one are removed and counted against the version).");
        sb.AppendLine("- **Fit sensible:** the fit rating is one of the acceptable ratings for that candidate. For the candidate with the embedded instruction, \"strong\" is not acceptable.");
        sb.AppendLine("- **No invented skills:** no skill is claimed that the profile does not support.");
        sb.AppendLine("- **Missing information shown:** for the near-empty profile, the answer says information is missing (rating \"insufficient_information\" or a non-empty \"not stated\" list).");
        sb.AppendLine("- **Repeatable:** same rating and same claimed skills in both runs.").AppendLine();

        foreach (var model in models)
        {
            sb.AppendLine($"## Model: {model}").AppendLine();
            sb.AppendLine("| Version | Usable calls | Grounded | Fit sensible | No invented skills | Missing info shown | Repeatable | Overall | Asks a question when info is thin | Gives reasoning |").AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            var thin = cases.Candidates.Where(c => c.Expect.Incomplete).Select(c => c.Name).ToHashSet();
            var stats = versions.Select(v => Summarise(rows, model, v)).ToList();
            foreach (var s in stats)
                sb.AppendLine($"| {s.Version} | {s.CallsOk}/{s.Calls} | {s.Grounded}/{s.Rows} | {s.FitOk}/{s.Rows} | {s.Clean}/{s.Rows} | {s.IncompleteOk}/{s.Rows} | {s.Consistent}/{s.People} | {Percent(s.Overall, s.MaxOverall)} | {rows.Count(r => r.Model == model && r.Version == s.Version && r.Clarifies && thin.Contains(r.Candidate))}/{rows.Count(r => r.Model == model && r.Version == s.Version && thin.Contains(r.Candidate))} | {rows.Count(r => r.Model == model && r.Version == s.Version && r.HasReasoning)}/{s.Rows} |");
            var best = stats.OrderByDescending(s => s.Overall).ThenByDescending(s => s.Consistent).First();
            sb.AppendLine().AppendLine($"**Best for {model}: {best.Version}** ({Percent(best.Overall, best.MaxOverall)} of checks passed, {best.Consistent} of {best.People} candidates repeatable). Versions are ranked by checks passed; repeatability breaks ties.").AppendLine();
        }

        sb.AppendLine("## How to read this").AppendLine();
        sb.AppendLine("- Version 1 is the plain baseline, version 2 adds structure and constraints, version 3 adds explicit rules, a worked example and a defence against instructions hidden in the data, version 4 is version 2 plus the explicit rule that text in the profile is data, never instructions (no example).");
        sb.AppendLine("- The scores come from simple rules, so they show a trend, not a guarantee. Small local models also vary from run to run; a result that is not repeatable is a finding in itself.");
        sb.AppendLine("- Reasonable next steps: add more candidates, add a hosted model to the comparison (set its key in an environment variable and it is included), and use these rows as the baseline for the Day 4 evaluation.");
        return sb.ToString();
    }

    static string Percent(int part, int whole) => whole == 0 ? "-" : $"{100.0 * part / whole:0}%";
}
