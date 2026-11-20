using System.Diagnostics;
using System.Text.Json;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.Services.Llm;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services.Tasks;

/// <summary>
/// Turns what a person types into tool calls, safely. Provider-neutral protocol: the model answers with one JSON object,
/// either {"tool", "arguments"} or {"reply"}. The model never acts: every tool runs in TaskTools under the signed-in
/// person's id, anything destructive waits for the person's explicit yes, and each call is written to the log.
/// </summary>
public sealed class TaskAssistantService(Db db, ILlmClient llm, LlmCallLedger ledger, TaskRepository tasks, TaskTools tools, AuditRepository audit, AppOptions options, ILogger<TaskAssistantService> logger)
{
    public const int MaxSteps = 4;
    public const string PromptVersion = "tasks-v1";
    static readonly TimeSpan ConfirmWindow = TimeSpan.FromMinutes(10);
    static readonly Lazy<string> SystemPrompt = new(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "tasks.v1.md")).Trim());

    sealed record Step(string? Tool, JsonElement Args, string ArgsJson, string? Reply);

    public Task<List<TaskView>> Tasks(Guid userId) => db.Read(s => tasks.List(s, userId, "all", 200));
    public Task<List<ToolCallView>> Log(Guid userId) => db.Read(s => tasks.Calls(s, userId, 50));

    public async Task<TaskReplyView> Message(AppUser user, string message, CancellationToken ct)
    {
        if (!llm.IsConfigured) throw new ApiException(503, "The assistant is not set up yet");

        // One reservation per message, under the same per-person lock and daily cap as the other assistant.
        var callId = await ledger.Reserve(user.Id, PromptVersion, "assistant requests");

        var actions = new List<ActionView>();
        var transcript = new List<string>();
        string? reply = null;
        PendingView? pending = null;
        int inTokens = 0, outTokens = 0, latency = 0, badReplies = 0;
        // One message may use up to four model calls; this is the limit on all of them together. The caller leaving still cancels everything.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TaskMessageDeadlineSeconds));
        try
        {
            for (var step = 0; step < MaxSteps && reply is null && pending is null; step++)
            {
                var answer = await llm.CompleteAsync(SystemPrompt.Value, BuildUser(message, transcript), deadline.Token);
                inTokens += answer.InputTokens; outTokens += answer.OutputTokens; latency += answer.LatencyMs;

                var parsed = Parse(answer.Text);
                if (parsed is null)
                {
                    if (++badReplies > 1) throw new UnusableOutputException("The model did not answer in the tool format");
                    transcript.Add("<note>Your last reply was not a single JSON object in one of the two allowed shapes. Reply with JSON only.</note>");
                    continue;
                }
                if (parsed.Reply is not null) { reply = parsed.Reply; break; }

                var clock = Stopwatch.StartNew();
                var result = await tools.Execute(user, parsed.Tool!, parsed.Args, confirmed: false);
                var took = (int)clock.ElapsedMilliseconds;
                await db.Read(async s => { await tasks.LogCall(s, user.Id, parsed.Tool!, parsed.ArgsJson, result.Outcome, result.Message, took); return 0; });
                actions.Add(new ActionView { Tool = parsed.Tool!, Outcome = result.Outcome, Summary = result.Message });

                if (result is { Outcome: "needs_confirmation", PendingSummary: { } summary })
                {
                    pending = await CreatePending(user, parsed, summary);
                    reply = $"I need your confirmation first: {summary}.";
                    break;
                }
                transcript.Add(ToolResultText(parsed.Tool!, result));
            }
            reply ??= actions.Count > 0 ? "I stopped after a few steps. The actions listed below are what was done." : "I could not work out what to do. Please say it another way.";
            await Finish(callId, answerModel: llm.Model, inTokens, outTokens, latency, "ok");
        }
        catch (LlmException e)
        {
            logger.LogWarning("Task assistant model call failed: {Kind}: {Message}", e.Kind, e.Message);
            await Finish(callId, llm.Model, null, null, null, e.Kind);
            throw LlmFailure.ToApi(e, LlmKeyScope.Current is not null);
        }
        catch (UnusableOutputException e)
        {
            logger.LogWarning("Task assistant answer refused: {Reason}", e.Message);
            await Finish(callId, llm.Model, inTokens, outTokens, latency, "invalid_output");
            throw new ApiException(502, "The model gave an answer we could not use. Try again.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The overall deadline ran out (the caller is still there). Whatever already ran stays done and is listed, so nothing is lost silently.
            logger.LogWarning("Task assistant message ran past its {Seconds} second deadline", options.TaskMessageDeadlineSeconds);
            await Finish(callId, llm.Model, null, null, null, "timeout");
            reply = actions.Count > 0
                ? "I ran out of time. The actions listed below are what was done; please check them and ask again for the rest."
                : "That took too long and nothing was done. Please try again.";
        }
        catch (OperationCanceledException)
        {
            await Finish(callId, llm.Model, null, null, null, "cancelled");
            throw;
        }
        return new TaskReplyView { Reply = reply, Actions = actions, Pending = pending };
    }

    /// <summary>The person's answer to a waiting confirmation. It is marked decided first, so it can never run twice; if the run then fails, the person asks again.</summary>
    public async Task<TaskReplyView> Confirm(AppUser user, Guid id, bool approve)
    {
        var (row, expired) = await db.Tx(async s =>
        {
            var r = await tasks.LockPending(s, id);
            if (r is null || r.UserId != user.Id) throw ApiException.NotFound(); // someone else's confirmation does not exist for you
            if (r.DecidedAt is not null) throw new ApiException(409, "This was already decided");
            await tasks.Decide(s, id);
            var late = r.ExpiresAt < DateTime.UtcNow;
            if (late) await tasks.LogCall(s, user.Id, r.Tool, r.Arguments, "expired", "The confirmation ran out of time", null);
            return (r, late);
        });
        // Thrown after the transaction has committed, so the "expired" decision and its log line are kept.
        if (expired) throw new ApiException(410, "This confirmation has expired. Ask again.");

        if (!approve)
        {
            await db.Read(async s => { await tasks.LogCall(s, user.Id, row.Tool, row.Arguments, "cancelled", row.Summary, null); return 0; });
            return new TaskReplyView { Reply = "Cancelled. Nothing was changed.", Actions = [new ActionView { Tool = row.Tool, Outcome = "cancelled", Summary = row.Summary }] };
        }

        using var args = JsonDocument.Parse(row.Arguments);
        var clock = Stopwatch.StartNew();
        // The arguments are checked again and the person's ownership is checked again: nothing is trusted just because it was stored.
        // What the person said yes to is also checked again: if the task was renamed in the meantime, they approved something else.
        var preview = await tools.Execute(user, row.Tool, args.RootElement, confirmed: false);
        var result = preview is { Outcome: "needs_confirmation" } && preview.PendingSummary != row.Summary
            ? new ToolResult("invalid", "The task changed since you were asked, so nothing was deleted. Ask again.")
            : await tools.Execute(user, row.Tool, args.RootElement, confirmed: true);
        var outcome = result.Outcome == "ok" ? "confirmed" : result.Outcome;
        await db.Tx(async s =>
        {
            await tasks.LogCall(s, user.Id, row.Tool, row.Arguments, outcome, result.Message, (int)clock.ElapsedMilliseconds);
            await audit.Record(s, user.Id, "task.confirmed." + row.Tool, id.ToString(), new { outcome });
            return 0;
        });
        return new TaskReplyView { Reply = result.Outcome == "ok" ? result.Message : "That could not be done. " + result.Message, Actions = [new ActionView { Tool = row.Tool, Outcome = outcome, Summary = result.Message }] };
    }

    async Task<PendingView> CreatePending(AppUser user, Step step, string summary)
    {
        var expires = DateTime.UtcNow + ConfirmWindow;
        var id = await db.Read(s => tasks.CreatePending(s, user.Id, step.Tool!, step.ArgsJson, summary, expires));
        return new PendingView { Id = id, Tool = step.Tool!, Summary = summary, ExpiresAt = expires };
    }

    Task Finish(long callId, string? answerModel, int? inTokens, int? outTokens, int? latency, string status) => ledger.Finish(callId, answerModel, inTokens, outTokens, latency, status);

    /// <summary>The request is the person's own words. Everything that came back from a tool is data and is escaped, so a task title cannot pose as an instruction.</summary>
    static string BuildUser(string message, List<string> transcript) =>
        $"Today is {DateTime.UtcNow:yyyy-MM-dd} ({DateTime.UtcNow:dddd}).\n<request>{PromptText.Escape(message)}</request>\n{string.Join("\n", transcript)}\nReply with the next JSON object.";

    static string ToolResultText(string tool, ToolResult r) =>
        $"<tool_result tool=\"{tool}\">{PromptText.Escape(JsonSerializer.Serialize(new { outcome = r.Outcome, message = r.Message, data = r.Data }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))}</tool_result>";

    static Step? Parse(string text)
    {
        using var doc = JsonText.Find(text, "tool") ?? JsonText.Find(text, "reply");
        if (doc is null) return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("tool", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 and <= 40 } name)
        {
            var args = root.TryGetProperty("arguments", out var a) ? a.Clone() : default;
            return new Step(name, args, args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText(), null);
        }
        if (root.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.String && r.GetString()?.Trim() is { Length: > 0 } reply)
            return new Step(null, default, "{}", reply.Length > 600 ? reply[..600] : reply);
        return null;
    }
}
