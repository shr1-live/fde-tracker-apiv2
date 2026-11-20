using FdeTracker.Api.Data;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

/// <summary>Read-only progress figures per person. Names are the display name or the part of the email before "@", never the full email.</summary>
public sealed class ReportRepository
{
    static string NameOf(string alias) => $"split_part(coalesce(nullif(btrim({alias}.display_name), ''), {alias}.email), '@', 1)";

    sealed class PersonRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Guid? BatchId { get; set; }
        public string? BatchName { get; set; }
        public string? BatchStartsOn { get; set; }
    }

    public Task<List<PersonRowView>> People(Session s, Guid? batch) =>
        s.Query<PersonRowView>(
            $"""
             select u.id, {NameOf("u")} as name, b.name as batch_name, to_char(b.starts_on, 'YYYY-MM-DD') as batch_starts_on,
               count(x.id) filter (where x.status = 'submitted')::int as submitted,
               count(x.id) filter (where x.status = 'approved')::int as approved,
               count(x.id) filter (where x.status = 'changes_requested')::int as changes_requested,
               count(x.id) filter (where x.status = 'draft')::int as drafts,
               round((avg(x.rating) filter (where x.status <> 'draft'))::numeric, 1)::float8 as avg_rating, -- drafts are private: not in the average
               greatest(max(x.submitted_at), max(x.reviewed_at), max(x.updated_at) filter (where x.status <> 'draft')) as last_activity
             from app_users u
             left join batches b on b.id = u.batch_id
             left join submissions x on x.user_id = u.id
             where u.access_status = 'approved' and (@batch::uuid is null or u.batch_id = @batch::uuid)
             group by u.id, b.id
             order by lower({NameOf("u")}), u.id
             limit 1000
             """, new { batch });

    /// <summary>Null when there is no approved person with that id.</summary>
    public async Task<ReportPersonView?> Person(Session s, Guid id)
    {
        var r = await s.First<PersonRow>(
            $"""
             select u.id, {NameOf("u")} as name, b.id as batch_id, b.name as batch_name, to_char(b.starts_on, 'YYYY-MM-DD') as batch_starts_on
             from app_users u left join batches b on b.id = u.batch_id
             where u.id = @id and u.access_status = 'approved'
             """, new { id });
        if (r is null) return null;
        return new ReportPersonView
        {
            Id = r.Id, Name = r.Name,
            Batch = r.BatchId is { } bid ? new BatchView { Id = bid, Name = r.BatchName ?? "", StartsOn = r.BatchStartsOn ?? "" } : null,
        };
    }

    public Task<List<ReportDayView>> Days(Session s, Guid userId) =>
        s.Query<ReportDayView>(
            $"""
             select x.task_key, x.status, x.rating, x.learned, x.note, x.submitted_at, x.reviewed_at, x.updated_at,
               (select count(*)::int from submission_items i where i.submission_id = x.id) as item_count,
               lr.decision as last_decision, lr.comment as last_comment, lr.reviewer as last_reviewer
             from submissions x
             left join lateral (
               select r.decision, r.comment, {NameOf("ru")} as reviewer
               from submission_reviews r join app_users ru on ru.id = r.reviewer_id
               where r.submission_id = x.id order by r.created_at desc limit 1
             ) lr on true
             where x.user_id = @userId
             """, new { userId });
}
