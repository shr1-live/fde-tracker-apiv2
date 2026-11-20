using System.Text;

namespace FdeTracker.Evals;

public static class Report
{
    static double Median(IEnumerable<int> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2.0;
    }

    /// <summary>Markdown report plus a recommendation. Rule: highest pass rate wins; ties go to the lower median latency.</summary>
    public static string Day1(List<CaseResult> results, string[] models, PromptSet set, string label, IReadOnlyDictionary<string, (double In, double Out)>? prices = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Day 1: model comparison on a fixed prompt set").AppendLine();
        sb.AppendLine($"Run on {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Prompt set version {set.Version}: the same {set.Cases.Count} prompts, same system prompt, temperature 0, for every model. Prompts: `evals/day1-prompts.json`. Raw results: `docs/evals/day1-{label}-results.json`.").AppendLine();

        var stats = models.Select(m =>
        {
            var rows = results.Where(r => r.Model == m).ToList();
            var ok = rows.Where(r => r.Error is null).ToList();
            return new
            {
                Model = m, Passed = rows.Count(r => r.Passed), Total = rows.Count, Errors = rows.Count(r => r.Error is not null),
                Median = Median(ok.Select(r => r.LatencyMs)), Max = ok.Count == 0 ? 0 : ok.Max(r => r.LatencyMs),
                TokensIn = rows.Sum(r => r.InputTokens), TokensOut = rows.Sum(r => r.OutputTokens),
            };
        }).ToList();

        sb.AppendLine("## Summary").AppendLine();
        // Cost = tokens x the price set in targets.json. No price set means no number (a local model has price 0; an unknown one is shown as "not set", never guessed).
        string Cost(string model, int tin, int tout) =>
            prices is not null && prices.TryGetValue(model, out var p) ? (p.In == 0 && p.Out == 0 ? "$0 (local)" : $"${(tin * p.In + tout * p.Out) / 1_000_000:0.0000}") : "not set";
        sb.AppendLine("| Model | Passed | Errors | Median latency | Slowest | Input tokens | Output tokens | Cost of this set |").AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var s in stats) sb.AppendLine($"| {s.Model} | {s.Passed}/{s.Total} | {s.Errors} | {s.Median / 1000:0.0} s | {s.Max / 1000.0:0.0} s | {s.TokensIn} | {s.TokensOut} | {Cost(s.Model, s.TokensIn, s.TokensOut)} |");

        sb.AppendLine().AppendLine("## Per prompt (pass / fail)").AppendLine();
        sb.AppendLine("| Prompt | Kind | " + string.Join(" | ", models) + " |").AppendLine("|---|---|" + string.Join("|", models.Select(_ => "---")) + "|");
        foreach (var c in set.Cases)
        {
            var cells = models.Select(m => results.FirstOrDefault(r => r.Model == m && r.CaseId == c.Id) is { } r ? (r.Error is not null ? "error" : r.Passed ? "pass" : "FAIL") : "-");
            sb.AppendLine($"| {c.Id} | {c.Kind} | {string.Join(" | ", cells)} |");
        }

        var failed = results.Where(r => !r.Passed).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine().AppendLine("## Failed answers").AppendLine();
            foreach (var f in failed) sb.AppendLine($"- **{f.Model} / {f.CaseId}**: {(f.Error ?? Trim(f.Answer)).Replace("\n", " ")}");
        }

        if (stats.Count == 0) return sb.AppendLine().AppendLine("No models were run.").ToString();
        // A model that errored is ruled out first, as the text below says; only if every model errored do we compare them all.
        var eligible = stats.Where(s => s.Errors == 0).ToList();
        var best = (eligible.Count > 0 ? eligible : stats).OrderByDescending(s => s.Passed).ThenBy(s => s.Median).First();
        var others = stats.Where(s => s.Model != best.Model).ToList();
        sb.AppendLine().AppendLine("## Recommendation").AppendLine();
        sb.Append($"Use **{best.Model}**. It passed {best.Passed} of {best.Total} prompts with a median latency of {best.Median / 1000:0.0} s");
        foreach (var o in others) sb.Append($", against {o.Model} at {o.Passed} of {o.Total} and {o.Median / 1000:0.0} s");
        sb.AppendLine(".").AppendLine();
        sb.AppendLine("Reasons, in order: (1) answer quality on this set, because a wrong answer costs more than a slow one; (2) speed, as the tiebreak; (3) reliability, since a model with errors or timeouts is ruled out first. Token counts show the cost side: a hosted model would be billed on the input and output tokens above.").AppendLine();
        sb.AppendLine($"Limits: {set.Cases.Count} prompts is a small sample and the checks are simple keyword rules, so treat this as a first comparison. Speed and cost depend on where a model runs: a local computer, or a hosted provider that bills per token. The first call to each model includes loading it into memory, so \"slowest\" is inflated. The keyword rules can still be too strict or too loose. Prompt set version 2 widened the accepted wording for refusals and added prompts; this was decided before the run, after set version 1 marked a fair refusal wrong, and the version 1 run is kept as a separate run for comparison. Rerun this after changing a model or prompt.");
        return sb.ToString();
    }

    static string Trim(string s) => s.Length > 160 ? s[..160] + "..." : s;
}
