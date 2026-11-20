using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class ContentRepository
{
    const string PublicCols = "c.id, c.title, c.summary, c.kind, c.url, c.stack_id, c.approved_at, c.recommended, t.slug as topic, t.title as topic_title, coalesce(u.display_name, split_part(u.email, '@', 1)) as author";

    // Fixed ORDER BY clauses picked by a validated key: no caller text ever reaches the SQL.
    static readonly Dictionary<string, string> OrderBy = new()
    {
        ["newest"] = "c.approved_at desc, c.id desc",
        ["oldest"] = "c.approved_at asc, c.id asc",
        ["title"] = "lower(c.title) asc, c.id asc",
        ["recommended"] = "c.recommended desc, c.approved_at desc, c.id desc",
    };

    /// <summary>Approved items: general ones for everyone plus the chosen stack's. Returns up to limit + 1 rows so the caller can tell if there is more.
    /// The cursor applies only to the "newest" order.</summary>
    public Task<List<ContentRecord>> Feed(Session s, string stack, string? topic, string sort, (DateTime Ts, Guid Id)? cursor, int limit)
    {
        var order = OrderBy[sort];
        var where = "c.status = 'approved' and (c.stack_id is null or c.stack_id = @stack)";
        if (topic is not null) where += " and t.slug = @topic";
        if (cursor is not null && sort == "newest") where += " and (c.approved_at, c.id) < (@ts::timestamptz, @cid::uuid)";
        return s.Query<ContentRecord>(
            $"""
             select {PublicCols} from content_items c join topics t on t.id = c.topic_id join app_users u on u.id = c.author_id
             where {where} order by {order} limit @take
             """,
            new { stack = stack == "general" ? null : stack, topic, ts = cursor?.Ts, cid = cursor?.Id, take = limit + 1 });
    }

    public Task<string?> LockStatus(Session s, Guid id) =>
        s.First<string?>("select status from content_items where id = @id for update", new { id });

    public Task SetRecommended(Session s, Guid id, bool recommended) =>
        s.Exec("update content_items set recommended = @recommended, recommended_at = case when @recommended then now() end where id = @id", new { id, recommended });

    public Task<List<MyContentView>> Mine(Session s, Guid userId) =>
        s.Query<MyContentView>(
            """
            select c.id, c.title, c.kind, c.status, c.stack_id, t.slug as topic, c.updated_at,
              (select comment from reviews r where r.content_id = c.id order by r.created_at desc limit 1) as last_review_comment
            from content_items c join topics t on t.id = c.topic_id where c.author_id = @userId order by c.updated_at desc limit 100
            """, new { userId });

    public Task<ContentRecord?> Find(Session s, Guid id) =>
        s.First<ContentRecord>(
            $"select {PublicCols}, c.body_md, c.status, c.author_id from content_items c join topics t on t.id = c.topic_id join app_users u on u.id = c.author_id where c.id = @id", new { id });

    public Task<int> ActiveCount(Session s, Guid userId) =>
        s.Scalar<int>("select count(*)::int from content_items where author_id = @userId and status in ('draft','in_review','changes_requested')", new { userId });

    public Task<int?> TopicId(Session s, string slug) => s.First<int?>("select id from topics where slug = @slug", new { slug });

    /// <summary>status is draft, in_review, or approved (a reviewer's or admin's own submission needs no review).</summary>
    public Task<ContentSavedView> Insert(Session s, int topicId, string? stackId, string kind, string title, string? summary, string? bodyMd, string? url, Guid authorId, string status) =>
        s.First<ContentSavedView>(
            """
            insert into content_items (topic_id, stack_id, kind, title, summary, body_md, url, author_id, status, submitted_at, approved_at)
            values (@topicId, @stackId, @kind, @title, @summary, @bodyMd, @url, @authorId, @status,
                    case when @status in ('in_review', 'approved') then now() end, case when @status = 'approved' then now() end)
            returning id, status
            """,
            new { topicId, stackId, kind, title, summary, bodyMd, url, authorId, status })!;

    public Task<ContentRow?> LockOwned(Session s, Guid id, Guid authorId) =>
        s.First<ContentRow>("select * from content_items where id = @id and author_id = @authorId for update", new { id, authorId });

    /// <summary>submitAs is null to keep the status, or the status a submission moves to (in_review, or approved for the review team).</summary>
    public Task<ContentSavedView> Update(Session s, Guid id, ContentRow next, string? submitAs) =>
        s.First<ContentSavedView>(
            """
            update content_items set topic_id=@TopicId, kind=@Kind, title=@Title, summary=@Summary, body_md=@BodyMd, url=@Url, stack_id=@StackId, updated_at=now(),
              status = coalesce(@submitAs, status),
              submitted_at = case when @submitAs is not null then now() else submitted_at end,
              approved_at = case when @submitAs = 'approved' then now() else approved_at end
            where id = @id returning id, status
            """,
            new { id, next.TopicId, next.Kind, next.Title, next.Summary, next.BodyMd, next.Url, next.StackId, submitAs })!;

    public async Task<bool> DeleteOwned(Session s, Guid id, Guid authorId) =>
        await s.Exec("delete from content_items where id = @id and author_id = @authorId and status in ('draft','changes_requested','rejected')", new { id, authorId }) > 0;
}
