using FdeTracker.Api.Data;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class BatchRepository
{
    const string Cols = "b.id, b.name, to_char(b.starts_on, 'YYYY-MM-DD') as starts_on";

    public Task<List<BatchListItemView>> List(Session s) =>
        s.Query<BatchListItemView>(
            $"""
             select {Cols}, (select count(*)::int from app_users u where u.batch_id = b.id) as members
             from batches b order by b.starts_on desc, b.name limit 500
             """);

    /// <summary>Null when a batch with that name already exists.</summary>
    public Task<BatchView?> Insert(Session s, string name, DateOnly startsOn) =>
        s.First<BatchView>(
            """
            insert into batches (name, starts_on) values (@name, @startsOn::date) on conflict (name) do nothing
            returning id, name, to_char(starts_on, 'YYYY-MM-DD') as starts_on
            """,
            new { name, startsOn = startsOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });

    public Task<BatchView?> ById(Session s, Guid id) => s.First<BatchView>($"select {Cols} from batches b where b.id = @id", new { id });
}
