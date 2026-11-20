using FdeTracker.Api.Common;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services.Llm;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services.Lab;

/// <summary>
/// The practice lab: runs the Week 1 Day 1 and Day 2 practicals from the web app on the active model (the server's, or the person's
/// own Gemini key when the request carries one), and scores the answers with exactly the evaluation tool's rules.
/// Every model call is reserved and logged like any assistant call, so it counts toward the same daily limit.
/// </summary>
public sealed class LabService(ILlmClient llm, AnalysisPipeline pipeline, LlmCallLedger ledger, LabData data, ILogger<LabService> logger)
{
    public const string Day1Label = "lab-day1";
    public const int MaxAnswerChars = 1000;
    /// <summary>Candidates per call, like the evaluation tool (the analysis allows up to 5).</summary>
    public const int Day2BatchSize = 4;

    public LabCasesView Day1Cases() => new()
    {
        System = data.Day1.System,
        Cases = data.Day1.Cases.Select(c => new LabCaseView { Id = c.Id, Kind = c.Kind, Prompt = c.Prompt }).ToList(),
    };

    public async Task<LabDay1ResultView> RunDay1(Guid userId, string caseId, CancellationToken ct)
    {
        var c = data.Day1.Cases.FirstOrDefault(x => x.Id == caseId) ?? throw ApiException.NotFound("There is no Day 1 case with that id");
        if (!llm.IsConfigured) throw new ApiException(503, "The assistant is not set up yet");

        var callId = await ledger.Reserve(userId, Day1Label, "model calls");
        LlmResult result;
        try
        {
            result = await llm.CompleteAsync(data.Day1.System, c.Prompt, ct);
        }
        catch (LlmException e)
        {
            logger.LogWarning("Lab Day 1 call failed: {Kind}", e.Kind); // the kind only: provider text never reaches the log or the person
            await ledger.Finish(callId, llm.Model, null, null, null, e.Kind);
            throw LlmFailure.ToApi(e, LlmKeyScope.Current is not null);
        }
        catch (OperationCanceledException)
        {
            await ledger.Finish(callId, llm.Model, null, null, null, "cancelled");
            throw;
        }

        var answer = Day1Scoring.ExtractAnswer(result.Text ?? "");
        var usable = !string.IsNullOrWhiteSpace(answer);
        var passed = usable && Day1Scoring.Score(c, answer);
        await ledger.Finish(callId, result.Model, result.InputTokens, result.OutputTokens, result.LatencyMs, usable ? "ok" : "invalid_output");
        var shown = answer.Trim();
        return new LabDay1ResultView
        {
            CaseId = c.Id, Kind = c.Kind, Model = result.Model ?? llm.Model ?? "", Passed = passed,
            Answer = shown.Length > MaxAnswerChars ? shown[..MaxAnswerChars] : shown,
            LatencyMs = result.LatencyMs, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens,
        };
    }

    public int Day2Batches => (data.Day2.Candidates.Count + Day2BatchSize - 1) / Day2BatchSize;

    public async Task<LabDay2ResultView> RunDay2(Guid userId, string version, int batch, CancellationToken ct)
    {
        if (!PromptLibrary.Versions.Contains(version)) throw ApiException.BadRequest($"Unknown prompt version. Use one of: {string.Join(", ", PromptLibrary.Versions)}.");
        var batches = Day2Batches;
        if (batch < 0 || batch >= batches) throw ApiException.BadRequest($"Batch must be from 0 to {batches - 1}.");
        if (!llm.IsConfigured) throw new ApiException(503, "The assistant is not set up yet");

        var group = data.Day2.Candidates.Skip(batch * Day2BatchSize).Take(Day2BatchSize).ToList();
        var people = group.Select(c => new CandidateInput { Name = c.Name, Profile = c.Profile }).ToList();
        var required = SkillTools.ExtractSkills(data.Day2.JobDescription);

        // Same pipeline, same guardrails and same logging as a real analysis.
        var callId = await ledger.Reserve(userId, "lab-day2-" + version, "model calls");
        LlmResult? result = null;
        try
        {
            result = await pipeline.Run(version, data.Day2.JobDescription, people, ct);
            // Scored exactly as the evaluation tool does (default guardrails, the model's own "not stated" list), whatever ASSISTANT_SUMMARY is.
            var (views, warnings) = AnalysisParser.Parse(result.Text, people, required);
            await ledger.Finish(callId, result.Model, result.InputTokens, result.OutputTokens, result.LatencyMs, "ok");
            var checks = Day2Scoring.Score(group, views);
            return View(version, batch, batches, result, group.Select((c, i) => Candidate(c, checks[i], Day2Scoring.Note(c, checks[i]))).ToList(), warnings);
        }
        catch (LlmException e)
        {
            logger.LogWarning("Lab Day 2 call failed: {Kind}", e.Kind);
            await ledger.Finish(callId, llm.Model, null, null, null, e.Kind);
            throw LlmFailure.ToApi(e, LlmKeyScope.Current is not null);
        }
        catch (OperationCanceledException)
        {
            await ledger.Finish(callId, llm.Model, null, null, null, "cancelled");
            throw;
        }
        catch (UnusableOutputException e)
        {
            // Not an error for the lab: an answer in the wrong shape is a result to learn from, and every candidate fails.
            logger.LogInformation("Lab Day 2 answer unusable: {Reason}", e.Message);
            await ledger.Finish(callId, result?.Model ?? llm.Model, result?.InputTokens, result?.OutputTokens, result?.LatencyMs, "invalid_output");
            const string note = "The model's answer could not be used (it was not in the agreed shape).";
            var failed = new Day2Check("", false, false, false, false, false, "", [], false, false);
            return View(version, batch, batches, result, group.Select(c => Candidate(c, failed, note)).ToList(), [note]);
        }
    }

    static LabDay2CandidateView Candidate(Day2Candidate c, Day2Check r, string note) => new()
    {
        Name = c.Name, Fit = r.Fit, ExpectedFit = c.Expect.Fit, ForbiddenClaims = c.Expect.MustNotClaim, Skills = r.Skills, Passed = r.Passed, Note = note,
    };

    LabDay2ResultView View(string version, int batch, int batches, LlmResult? result, List<LabDay2CandidateView> candidates, List<string> warnings) => new()
    {
        Version = version, Model = result?.Model ?? llm.Model ?? "", Batch = batch, Batches = batches,
        LatencyMs = result?.LatencyMs ?? 0, InputTokens = result?.InputTokens ?? 0, OutputTokens = result?.OutputTokens ?? 0,
        Candidates = candidates, Passed = candidates.Count(c => c.Passed), Total = candidates.Count, Warnings = warnings,
    };
}
