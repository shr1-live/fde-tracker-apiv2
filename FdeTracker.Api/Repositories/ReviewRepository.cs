using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class ReviewRepository
{
    public Task<List<ReviewQueueItemView>> Queue(Session s) =>
        s.Query<ReviewQueueItemView>(
            """
            select c.id, c.title, c.kind, c.url, c.summary, c.stack_id, c.submitted_at, t.slug as topic, t.title as topic_title,
                   coalesce(u.display_name, split_part(u.email,'@',1)) as author
            from content_items c join topics t on t.id = c.topic_id join app_users u on u.id = c.author_id
            where c.status = 'in_review' order by c.submitted_at limit 100
            """);

    public Task<ReviewTarget?> LockTarget(Session s, Guid id) =>
        s.First<ReviewTarget>("select id, author_id, status from content_items where id = @id for update", new { id });

    public Task SetStatus(Session s, Guid id, string status) =>
        s.Exec("update content_items set status = @status, approved_at = case when @status = 'approved' then now() end, updated_at = now() where id = @id", new { id, status });

    public Task AddReview(Session s, Guid contentId, Guid reviewerId, string decision, string? comment) =>
        s.Exec("insert into reviews (content_id, reviewer_id, decision, comment) values (@contentId, @reviewerId, @decision, @comment)", new { contentId, reviewerId, decision, comment });
}
