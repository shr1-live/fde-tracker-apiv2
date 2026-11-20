using FdeTracker.Api.Data;

namespace FdeTracker.Api.Repositories;

/// <summary>A saved lab run as stored. The result comes back as JSON text and is parsed, never interpreted.</summary>
public sealed class LabRunRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Day { get; set; }
    public string Label { get; set; } = "";
    public string[] Models { get; set; } = [];
    public string ResultJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public bool IsSample { get; set; }
}

public sealed class LabRunRepository
{
    const string Cols = "id, user_id, day::int as day, label, models, result::text as result_json, created_at, is_sample";

    /// <summary>Serialises one person's saves so the count check and the insert cannot interleave.</summary>
    public Task LockUser(Session s, Guid userId) => s.Exec("select pg_advisory_xact_lock(hashtext(@key))", new { key = "lab:" + userId });

    /// <summary>Serialises pinning, so two admins pinning at once cannot leave two samples for one day.</summary>
    public Task LockSamples(Session s) => s.Exec("select pg_advisory_xact_lock(hashtext('lab:samples'))");

    public Task<int> CountForUser(Session s, Guid userId) => s.Scalar<int>("select count(*)::int from lab_runs where user_id = @userId", new { userId });

    public async Task<LabRunRow> Insert(Session s, Guid userId, int day, string label, string[] models, string resultJson) =>
        (await s.First<LabRunRow>(
            $"insert into lab_runs (user_id, day, label, models, result) values (@userId, @day::smallint, @label, @models, @resultJson::jsonb) returning {Cols}",
            new { userId, day, label, models, resultJson }))!;

    public Task<List<LabRunRow>> Mine(Session s, Guid userId, int limit) =>
        s.Query<LabRunRow>($"select {Cols} from lab_runs where user_id = @userId order by created_at desc, id limit @limit", new { userId, limit });

    public Task<LabRunRow?> ById(Session s, Guid id, bool forUpdate = false) =>
        s.First<LabRunRow>($"select {Cols} from lab_runs where id = @id{(forUpdate ? " for update" : "")}", new { id });

    public Task<int> Delete(Session s, Guid id, Guid userId) => s.Exec("delete from lab_runs where id = @id and user_id = @userId", new { id, userId });

    public Task<List<LabRunRow>> Samples(Session s) => s.Query<LabRunRow>($"select {Cols} from lab_runs where is_sample order by day");

    /// <summary>Makes the run the sample for its day; the day's previous sample is unpinned first (same transaction).</summary>
    public async Task Pin(Session s, Guid id, int day)
    {
        await s.Exec("update lab_runs set is_sample = false where day = @day::smallint and is_sample and id <> @id", new { id, day });
        await s.Exec("update lab_runs set is_sample = true where id = @id", new { id });
    }

    public Task Unpin(Session s, Guid id) => s.Exec("update lab_runs set is_sample = false where id = @id", new { id });
}
