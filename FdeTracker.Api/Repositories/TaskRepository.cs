using FdeTracker.Api.Data;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class PendingRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Tool { get; set; } = "";
    public string Arguments { get; set; } = "{}";
    public string Summary { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Every query here is scoped by user_id taken from the sign-in, never from anything the model sent.</summary>
public sealed class TaskRepository
{
    const string Cols = "id, title, due_date::text as due_date, priority, status, notes";

    public Task<int> Count(Session s, Guid userId) => s.Scalar<int>("select count(*)::int from assistant_tasks where user_id = @userId", new { userId });

    public Task<TaskView> Create(Session s, Guid userId, string title, string? due, string priority, string? notes) =>
        s.First<TaskView>($"insert into assistant_tasks (user_id, title, due_date, priority, notes) values (@userId, @title, @due::date, @priority, @notes) returning {Cols}", new { userId, title, due, priority, notes })!;

    public Task<List<TaskView>> List(Session s, Guid userId, string status, int limit) =>
        s.Query<TaskView>(
            $"select {Cols} from assistant_tasks where user_id = @userId and (@status = 'all' or status = @status) order by status, due_date nulls last, created_at limit @limit",
            new { userId, status, limit });

    public Task<TaskView?> Find(Session s, Guid userId, Guid id) =>
        s.First<TaskView>($"select {Cols} from assistant_tasks where id = @id and user_id = @userId", new { id, userId });

    /// <summary>Changes only the fields that were sent. Returns null when the task is not this person's.</summary>
    public Task<TaskView?> Update(Session s, Guid userId, Guid id, bool setTitle, string? title, bool setDue, string? due, bool setPriority, string? priority, bool setStatus, string? status, bool setNotes, string? notes) =>
        s.First<TaskView>(
            $"""
            update assistant_tasks set
              title = case when @setTitle then @title else title end,
              due_date = case when @setDue then @due::date else due_date end,
              priority = case when @setPriority then @priority else priority end,
              status = case when @setStatus then @status else status end,
              notes = case when @setNotes then @notes else notes end,
              updated_at = now()
            where id = @id and user_id = @userId
            returning {Cols}
            """, new { id, userId, setTitle, title, setDue, due, setPriority, priority, setStatus, status, setNotes, notes });

    public async Task<bool> Delete(Session s, Guid userId, Guid id) =>
        await s.Exec("delete from assistant_tasks where id = @id and user_id = @userId", new { id, userId }) > 0;

    // ---- waiting confirmations
    public Task<Guid> CreatePending(Session s, Guid userId, string tool, string argumentsJson, string summary, DateTime expiresAt) =>
        s.Scalar<Guid>("insert into pending_actions (user_id, tool, arguments, summary, expires_at) values (@userId, @tool, @argumentsJson::jsonb, @summary, @expiresAt) returning id", new { userId, tool, argumentsJson, summary, expiresAt });

    public Task<PendingRow?> LockPending(Session s, Guid id) =>
        s.First<PendingRow>("select id, user_id, tool, arguments::text as arguments, summary, expires_at, decided_at from pending_actions where id = @id for update", new { id });

    public Task Decide(Session s, Guid id) => s.Exec("update pending_actions set decided_at = now() where id = @id", new { id });

    // ---- the execution log
    public Task LogCall(Session s, Guid userId, string tool, string? argumentsJson, string outcome, string? detail, int? latencyMs) =>
        s.Exec("insert into tool_calls (user_id, tool, arguments, outcome, detail, latency_ms) values (@userId, @tool, @argumentsJson::jsonb, @outcome, @detail, @latencyMs)",
            new { userId, tool, argumentsJson, outcome, detail = detail is { Length: > 300 } ? detail[..300] : detail, latencyMs });

    public Task<List<ToolCallView>> Calls(Session s, Guid userId, int limit) =>
        s.Query<ToolCallView>("select tool, arguments::text as arguments, outcome, detail, latency_ms, created_at from tool_calls where user_id = @userId order by id desc limit @limit", new { userId, limit });
}
