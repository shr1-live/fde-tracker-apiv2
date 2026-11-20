using FdeTracker.Api.Common;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Evals;

public sealed record CaseResult(string Model, string CaseId, string Kind, bool Passed, string Answer, int LatencyMs, int InputTokens, int OutputTokens, string? Error);

/// <summary>Day 1: run one fixed prompt set against several models and score each answer automatically (scoring rules: <see cref="Day1Scoring"/>, shared with the web practice lab).</summary>
public static class Day1Runner
{
    public static async Task<List<CaseResult>> Run(PromptSet set, IReadOnlyList<ResolvedTarget> targets, int timeoutSeconds)
    {
        var results = new List<CaseResult>();
        using var http = new HttpClient(new SocketsHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var target in targets)
        {
            var model = target.Name; // results are labelled with the target name
            var client = LlmClientFactory.Create(target.Provider, http, new AppOptions { LlmProvider = target.Provider, LlmBaseUrl = string.IsNullOrWhiteSpace(target.BaseUrl) ? null : target.BaseUrl, LlmApiKey = target.ApiKey, LlmModel = target.Model, LlmTimeoutSeconds = timeoutSeconds, LlmTotalTimeoutSeconds = timeoutSeconds });
            foreach (var c in set.Cases)
            {
                Console.Write($"{model,-14} {c.Id,-20} ");
                try
                {
                    var r = await client.CompleteAsync(set.System, c.Prompt, CancellationToken.None);
                    var answer = Day1Scoring.ExtractAnswer(r.Text);
                    var ok = Day1Scoring.Score(c, answer);
                    results.Add(new CaseResult(model, c.Id, c.Kind, ok, answer, r.LatencyMs, r.InputTokens, r.OutputTokens, null));
                    Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {r.LatencyMs} ms");
                }
                catch (Exception e) when (e is LlmException or HttpRequestException or TaskCanceledException)
                {
                    var kind = e is LlmException le ? le.Kind : e.GetType().Name;
                    results.Add(new CaseResult(model, c.Id, c.Kind, false, "", 0, 0, 0, $"{kind}: {e.Message}")); // keep going: one failure must not lose the rest
                    Console.WriteLine($"ERROR {kind}");
                }
            }
        }
        return results;
    }
}
