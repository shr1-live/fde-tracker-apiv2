using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Repositories;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// The daily limit and usage log for every model call a person makes (assistant, task assistant, practice lab).
/// A call is reserved before anything is sent, under a per-person lock, so parallel or aborted requests still count.
/// Own-key requests use their own, higher limit (<see cref="LlmKeyScope.CapFor"/>).
/// </summary>
public sealed class LlmCallLedger(Db db, LlmCallRepository calls, AppOptions options)
{
    /// <param name="label">Stored in llm_calls.prompt_version (for example "v4", "tasks-v1", "lab-day1").</param>
    /// <param name="what">Plain words for the limit message, for example "analyses".</param>
    public Task<long> Reserve(Guid userId, string label, string what) => db.Tx(async s =>
    {
        await calls.LockUser(s, userId);
        var ownKey = LlmKeyScope.Current is not null;
        var cap = LlmKeyScope.CapFor(options); // own-key calls are counted apart from the shared limit
        if (await calls.CountLastDay(s, userId, ownKey) >= cap)
            // kind "daily_limit" tells the web app this is not a short wait like a provider's per-minute limit: stop, do not retry
            throw new ApiException(429, $"Daily limit of {cap} {what} reached. Try again tomorrow.") { Category = "quota", Kind = "daily_limit", Retryable = false };
        return await calls.Reserve(s, userId, label, ownKey);
    });

    public Task Finish(long callId, string? model, int? inTokens, int? outTokens, int? latency, string status) =>
        db.Read(async s => { await calls.Finish(s, callId, model, inTokens, outTokens, latency, status); return 0; });
}
