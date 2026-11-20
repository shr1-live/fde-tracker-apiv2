using FdeTracker.Api.Common;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Evals;

/// <summary>One candidate in one call: which checks passed. Failed calls produce rows with Parsed = false.</summary>
public sealed record Day2Row(string Model, string Version, int Repeat, string Candidate, bool Parsed, bool Present, bool Grounded, bool FitOk, bool NoForbidden, bool IncompleteOk, string Fit, string[] Skills, int LatencyMs, int InputTokens, int OutputTokens, string? Error, int Batch = 0, bool Clarifies = false, bool HasReasoning = false);

/// <summary>Day 2: every stored prompt version, on the same job and candidates, repeated to see whether results stay the same.</summary>
public static class Day2Runner
{
    public const int Repeats = 2;
    public const int BatchSize = 4; // candidates per call, like a real request (the API allows up to 5)

    public static async Task<List<Day2Row>> Run(Day2Cases cases, IReadOnlyList<ResolvedTarget> targets, int timeoutSeconds)
    {
        var rows = new List<Day2Row>();
        var required = SkillTools.ExtractSkills(cases.JobDescription);
        var prompts = new PromptLibrary();
        // EVAL_VERSIONS=v5,v6,v7 runs only those (the earlier versions' results are kept from an earlier run); default is every version.
        var only = Environment.GetEnvironmentVariable("EVAL_VERSIONS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var versions = PromptLibrary.Versions.Where(v => only is null || only.Contains(v)).ToArray();
        using var http = new HttpClient(new SocketsHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };

        foreach (var target in targets)
        {
            var client = LlmClientFactory.Create(target.Provider, http, new AppOptions { LlmProvider = target.Provider, LlmBaseUrl = string.IsNullOrWhiteSpace(target.BaseUrl) ? null : target.BaseUrl, LlmApiKey = target.ApiKey, LlmModel = target.Model, LlmTimeoutSeconds = timeoutSeconds, LlmTotalTimeoutSeconds = timeoutSeconds });
            var pipeline = new AnalysisPipeline(client, prompts);
            foreach (var version in versions)
                for (var repeat = 1; repeat <= Repeats; repeat++)
                    foreach (var (group, batch) in cases.Candidates.Chunk(BatchSize).Select((g, i) => (g, i + 1)))
                    {
                        var part = new Day2Cases { JobDescription = cases.JobDescription, Candidates = [.. group] };
                        var people = group.Select(c => new CandidateInput { Name = c.Name, Profile = c.Profile }).ToList();
                        Console.Write($"{target.Name,-14} {version} run {repeat} batch {batch}  ");
                        try
                        {
                            var r = await pipeline.Run(version, cases.JobDescription, people, CancellationToken.None);
                            rows.AddRange(Score(target.Name, version, repeat, part, people, required, r, batch));
                            Console.WriteLine($"ok  {r.LatencyMs} ms");
                        }
                        catch (Exception e) when (e is LlmException or HttpRequestException or TaskCanceledException)
                        {
                            rows.AddRange(group.Select(c => Failed(target.Name, version, repeat, c.Name, e is LlmException le ? $"{le.Kind}: {le.Message}" : e.GetType().Name, batch)));
                            Console.WriteLine("ERROR " + (e is LlmException le2 ? $"{le2.Kind}: {le2.Message}" : e.GetType().Name + ": " + e.Message));
                        }
                    }
        }
        return rows;
    }

    static Day2Row Failed(string model, string version, int repeat, string name, string error, int batch = 0) =>
        new(model, version, repeat, name, false, false, false, false, false, false, "", [], 0, 0, 0, error, batch);

    static IEnumerable<Day2Row> Score(string model, string version, int repeat, Day2Cases cases, List<CandidateInput> people, List<string> required, LlmResult r, int batch)
    {
        List<FdeTracker.Api.ViewModels.CandidateAnalysisView> views;
        try { views = AnalysisParser.Parse(r.Text, people, required).Candidates; }
        catch (UnusableOutputException)
        {
            return cases.Candidates.Select(c => Failed(model, version, repeat, c.Name, "unusable output", batch)).ToList();
        }

        // The checks themselves live in Day2Scoring, shared with the web practice lab.
        return Day2Scoring.Score(cases.Candidates, views).Select(x =>
            new Day2Row(model, version, repeat, x.Name, true, x.Present, x.Grounded, x.FitOk, x.NoForbidden, x.IncompleteOk, x.Fit, x.Skills, r.LatencyMs, r.InputTokens, r.OutputTokens, null, batch, x.Clarifies, x.HasReasoning)).ToList();
    }
}
