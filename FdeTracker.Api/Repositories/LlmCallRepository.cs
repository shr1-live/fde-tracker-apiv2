using FdeTracker.Api.Data;

namespace FdeTracker.Api.Repositories;

public sealed class LlmCallRepository
{
    /// <summary>Serialises one person's reservations so the cap check and the insert cannot interleave.</summary>
    public Task LockUser(Session s, Guid userId) => s.Exec("select pg_advisory_xact_lock(hashtext(@key))", new { key = "llm:" + userId });

    /// <summary>
    /// Calls in the last 24 hours, reserved and failed ones included, so a loop of bad requests cannot run up the bill.
    /// Server-model calls and own-key calls are counted apart: each has its own limit, and one never uses up the other.
    /// </summary>
    public Task<int> CountLastDay(Session s, Guid userId, bool ownKey) =>
        s.Scalar<int>("select count(*)::int from llm_calls where user_id = @userId and own_key = @ownKey and created_at > now() - interval '1 day'", new { userId, ownKey });

    public Task<long> Reserve(Session s, Guid userId, string promptVersion, bool ownKey) =>
        s.Scalar<long>("insert into llm_calls (user_id, prompt_version, status, own_key) values (@userId, @promptVersion, 'pending', @ownKey) returning id", new { userId, promptVersion, ownKey });

    // The statuses the table accepts (migrations 002 and 006). A kind added later and not listed here is recorded as provider_error, so logging can never turn a model failure into a server error.
    static readonly HashSet<string> Known = ["pending", "ok", "invalid_output", "provider_error", "timeout", "cancelled", "rate_limited", "server_error", "network", "auth", "bad_request", "too_large", "model_not_found", "content_blocked", "bad_format", "truncated"];

    public Task Finish(Session s, long id, string? model, int? inputTokens, int? outputTokens, int? latencyMs, string status) =>
        s.Exec("update llm_calls set model = @model, input_tokens = @inputTokens, output_tokens = @outputTokens, latency_ms = @latencyMs, status = @status where id = @id",
            new { id, model, inputTokens, outputTokens, latencyMs, status = Known.Contains(status) ? status : "provider_error" });
}
