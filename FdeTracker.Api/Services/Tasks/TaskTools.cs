using System.Globalization;
using System.Text.Json;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;

namespace FdeTracker.Api.Services.Tasks;

/// <summary>What a tool did. Outcome is ok, invalid, denied, failed or needs_confirmation.</summary>
public sealed record ToolResult(string Outcome, string Message, object? Data = null, string? PendingSummary = null);

/// <summary>
/// The four tools. They run here, in application code, for the signed-in person only: the model supplies arguments,
/// never an identity. Arguments are checked strictly (unknown fields are refused), a task that is not the person's looks
/// like it does not exist, a delete does nothing until it has been confirmed, and any unexpected error becomes a plain
/// "failed" with nothing changed.
/// </summary>
public sealed class TaskTools(Db db, TaskRepository tasks)
{
    public const int MaxTasksPerUser = 200;
    public static readonly string[] Names = ["create_task", "list_tasks", "update_task", "delete_task"];
    static readonly string[] Priorities = ["low", "medium", "high"];
    static readonly string[] Statuses = ["open", "done"];

    public static bool IsDestructive(string tool) => tool == "delete_task";

    /// <summary>Raised by the argument checks to stop with a message the model can act on. Never escapes Execute.</summary>
    sealed class ArgumentProblem(string message) : Exception(message);

    public async Task<ToolResult> Execute(AppUser user, string tool, JsonElement args, bool confirmed)
    {
        try
        {
            return tool switch
            {
                "create_task" => await Create(user, args),
                "list_tasks" => await List(user, args),
                "update_task" => await Update(user, args),
                "delete_task" => await Delete(user, args, confirmed),
                _ => new ToolResult("invalid", $"Unknown tool. Use one of: {string.Join(", ", Names)}."),
            };
        }
        catch (ArgumentProblem p) { return new ToolResult("invalid", p.Message); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new ToolResult("failed", "The tool failed and nothing was changed."); // details stay out of what the model and the person see
        }
    }

    async Task<ToolResult> Create(AppUser user, JsonElement raw)
    {
        var a = new Args(raw, "title", "due_date", "priority", "notes");
        var title = a.Text("title", 1, 140, required: true)!;
        var due = a.Date("due_date");
        var priority = a.OneOf("priority", Priorities) ?? "medium";
        var notes = a.Text("notes", 0, 500);
        var task = await db.Tx(async s =>
        {
            await s.Exec("select pg_advisory_xact_lock(hashtextextended(@key, 0))", new { key = "tasks:" + user.Id });
            return await tasks.Count(s, user.Id) >= MaxTasksPerUser ? null : await tasks.Create(s, user.Id, title, due, priority, notes);
        });
        return task is null
            ? new ToolResult("invalid", $"You already have {MaxTasksPerUser} tasks. Delete or finish some first.")
            : new ToolResult("ok", $"Created task \"{task.Title}\".", task);
    }

    async Task<ToolResult> List(AppUser user, JsonElement raw)
    {
        var a = new Args(raw, "status", "limit");
        // Found by running a real model: with "open" as the default, a task that was already marked done could not be found
        // again, so "delete the task I just finished" went nowhere. With no status given, every task is listed.
        var status = a.OneOf("status", [.. Statuses, "all"]) ?? "all";
        var limit = a.Int("limit", 1, 50, 20);
        var list = await db.Read(s => tasks.List(s, user.Id, status, limit));
        return new ToolResult("ok", $"{list.Count} {(status == "all" ? "" : status + " ")}task(s).", list);
    }

    async Task<ToolResult> Update(AppUser user, JsonElement raw)
    {
        var a = new Args(raw, "id", "title", "due_date", "priority", "status", "notes");
        var id = a.Id();
        var setTitle = a.Has("title"); var setDue = a.Has("due_date"); var setPriority = a.Has("priority"); var setStatus = a.Has("status"); var setNotes = a.Has("notes");
        if (!(setTitle || setDue || setPriority || setStatus || setNotes)) throw new ArgumentProblem("Give at least one thing to change: title, due_date, priority, status or notes.");
        var title = setTitle ? a.Text("title", 1, 140, required: true) : null;
        var due = setDue ? a.Date("due_date") : null;
        var priority = setPriority ? a.OneOf("priority", Priorities, required: true) : null;
        var status = setStatus ? a.OneOf("status", Statuses, required: true) : null;
        var notes = setNotes ? a.Text("notes", 0, 500) : null;
        var task = await db.Read(s => tasks.Update(s, user.Id, id, setTitle, title, setDue, due, setPriority, priority, setStatus, status, setNotes, notes));
        return task is null ? NotFound() : new ToolResult("ok", $"Updated task \"{task.Title}\".", task);
    }

    async Task<ToolResult> Delete(AppUser user, JsonElement raw, bool confirmed)
    {
        var id = new Args(raw, "id").Id();
        var task = await db.Read(s => tasks.Find(s, user.Id, id));
        if (task is null) return NotFound();
        if (!confirmed) return new ToolResult("needs_confirmation", $"Delete the task \"{task.Title}\"?", PendingSummary: $"Delete the task \"{task.Title}\"");
        var gone = await db.Read(s => tasks.Delete(s, user.Id, id));
        return gone ? new ToolResult("ok", $"Deleted task \"{task.Title}\".") : NotFound();
    }

    // Someone else's task and a task that does not exist look the same, so ids cannot be probed.
    static ToolResult NotFound() => new("denied", "No task with that id.");

    /// <summary>A strict reader for the model's arguments.</summary>
    sealed class Args
    {
        readonly JsonElement _o;

        public Args(JsonElement el, params string[] allowed)
        {
            _o = el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? JsonDocument.Parse("{}").RootElement : el;
            if (_o.ValueKind != JsonValueKind.Object) throw new ArgumentProblem("arguments must be a JSON object.");
            foreach (var p in _o.EnumerateObject())
                if (!allowed.Contains(p.Name)) throw new ArgumentProblem($"Unknown argument '{Clip(p.Name)}'. Allowed: {string.Join(", ", allowed)}.");
        }

        public bool Has(string name) => _o.TryGetProperty(name, out _);

        public string? Text(string name, int min, int max, bool required = false)
        {
            if (!_o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
                return required ? throw new ArgumentProblem($"{name} is required.") : null;
            if (v.ValueKind != JsonValueKind.String) throw new ArgumentProblem($"{name} must be text.");
            var t = (v.GetString() ?? "").Trim();
            if (t.Length < min || t.Length > max) throw new ArgumentProblem(min == 0 ? $"{name} can be at most {max} characters." : $"{name} must be {min} to {max} characters.");
            return t;
        }

        public string? Date(string name)
        {
            var t = Text(name, 0, 10);
            if (t is null || t.Length == 0) return null;
            if (!DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year is < 2000 or > 2100)
                throw new ArgumentProblem($"{name} must be a real date written YYYY-MM-DD (years 2000 to 2100).");
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public string? OneOf(string name, string[] allowed, bool required = false)
        {
            var t = Text(name, 0, 20, required);
            if (t is null) return null;
            return allowed.Contains(t) ? t : throw new ArgumentProblem($"{name} must be one of: {string.Join(", ", allowed)}.");
        }

        public Guid Id()
        {
            var t = Text("id", 1, 40, required: true)!;
            return Guid.TryParseExact(t, "D", out var g) ? g : throw new ArgumentProblem("id must be a task id from list_tasks.");
        }

        public int Int(string name, int min, int max, int fallback)
        {
            if (!_o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return fallback;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < min || n > max) throw new ArgumentProblem($"{name} must be a whole number from {min} to {max}.");
            return n;
        }

        static string Clip(string s) => s.Length > 30 ? s[..30] : s;
    }
}
