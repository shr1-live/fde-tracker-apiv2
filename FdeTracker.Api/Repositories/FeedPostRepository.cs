using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

/// <summary>The shared feed (feed_posts, feed_likes, feed_comments). Parameterised SQL only.</summary>
public sealed class FeedPostRepository
{
    static readonly string Name = LearningProgramRepository.NameOf("u");

    const string PostCols =
        """
        p.id, p.author_id, p.body, p.url, p.created_at, p.edited_at, p.like_count, p.comment_count,
        exists (select 1 from feed_likes l where l.post_id = p.id and l.user_id = @me) as liked_by_me
        """;

    /// <summary>Who may see a post: the admin, or a member of the post's program. Needs @me and @admin.</summary>
    const string Visible = "(@admin or p.program_id is null or exists (select 1 from lp_members vm where vm.program_id = p.program_id and vm.user_id = @me))";

    /// <summary>One page of the feed in the given order, after the cursor (if any). Reads one row more than asked so the caller knows if there is a next page.
    /// The SQL is built only from fixed fragments; every value is a parameter.</summary>
    public Task<List<FeedPostRow>> Page(Session s, Guid me, bool admin, Guid? programId, FeedPostQuery q, int take)
    {
        var where = new List<string> { Visible };
        if (programId is not null) where.Add("p.program_id = @programId");
        if (q.Kind is not null) where.Add("p.kind = @kind");
        if (q.Mine) where.Add("p.author_id = @me");
        if (q.Pattern is not null) where.Add("(p.body ilike @pattern escape '!' or p.url ilike @pattern escape '!')");
        var (order, after) = q.Sort switch
        {
            FeedSort.Top => ("p.like_count desc, p.created_at desc, p.id desc", "(p.like_count, p.created_at, p.id) < (@count, @at, @id)"),
            FeedSort.Discussed => ("p.comment_count desc, p.created_at desc, p.id desc", "(p.comment_count, p.created_at, p.id) < (@count, @at, @id)"),
            _ => ("p.created_at desc, p.id desc", "(p.created_at, p.id) < (@at, @id)"),
        };
        if (q.After is not null) where.Add(after);
        var filter = where.Count == 0 ? "" : "where " + string.Join(" and ", where);
        return s.Query<FeedPostRow>(
            $"""
             select {PostCols}, {Name} as author_name
             from feed_posts p join app_users u on u.id = p.author_id
             {filter}
             order by {order}
             limit @take
             """,
            new { me, admin, programId, kind = q.Kind, pattern = q.Pattern, count = q.After?.Count ?? 0, at = q.After?.At, id = q.After?.Id, take });
    }

    public Task<FeedPostRow?> Post(Session s, Guid me, bool admin, Guid id) =>
        s.First<FeedPostRow>(
            $"select {PostCols}, {Name} as author_name from feed_posts p join app_users u on u.id = p.author_id where p.id = @id and {Visible}",
            new { me, admin, id });

    /// <summary>The last <paramref name="perPost"/> comments of each post, oldest first.</summary>
    public Task<List<FeedCommentRow>> LastComments(Session s, Guid[] postIds, int perPost) =>
        s.Query<FeedCommentRow>(
            $"""
             select x.id, x.post_id, x.author_id, x.author_name, x.body, x.created_at from (
               select c.id, c.post_id, c.author_id, {Name} as author_name, c.body, c.created_at,
                 row_number() over (partition by c.post_id order by c.created_at desc, c.id desc) as rn
               from feed_comments c join app_users u on u.id = c.author_id
               where c.post_id = any(@postIds)
             ) x where x.rn <= @perPost
             order by x.post_id, x.created_at, x.id
             """, new { postIds, perPost });

    public Task<FeedPostEditRow?> PostForEdit(Session s, Guid id) =>
        s.First<FeedPostEditRow>("select author_id, body, url from feed_posts where id = @id for update", new { id });

    public Task UpdatePost(Session s, Guid id, string? body, string? url) =>
        s.Exec("update feed_posts set body = @body, url = @url, edited_at = now() where id = @id", new { id, body, url });

    /// <summary>Serialises one person's writes to the feed so the daily limits cannot be raced.</summary>
    public Task LockAuthor(Session s, Guid userId) =>
        s.Exec("select pg_advisory_xact_lock(7002, hashtext(@key))", new { key = userId.ToString() });

    public Task<int> PostsToday(Session s, Guid userId) =>
        s.Scalar<int>("select count(*)::int from feed_posts where author_id = @userId and created_at >= date_trunc('day', now() at time zone 'utc') at time zone 'utc'", new { userId });

    public Task<int> CommentsToday(Session s, Guid userId) =>
        s.Scalar<int>("select count(*)::int from feed_comments where author_id = @userId and created_at >= date_trunc('day', now() at time zone 'utc') at time zone 'utc'", new { userId });

    public Task<Guid> InsertPost(Session s, Guid authorId, Guid? programId, string? body, string? url) =>
        s.Scalar<Guid>("insert into feed_posts (author_id, program_id, body, url) values (@authorId, @programId, @body, @url) returning id", new { authorId, programId, body, url });

    /// <summary>The author of a post the caller can see; null when it does not exist or is in someone else's program.</summary>
    public Task<Guid?> PostAuthor(Session s, Guid me, bool admin, Guid id) =>
        s.First<Guid?>($"select p.author_id from feed_posts p where p.id = @id and {Visible}", new { me, admin, id });

    public Task DeletePost(Session s, Guid id) => s.Exec("delete from feed_posts where id = @id", new { id });

    /// <summary>Likes the post, or takes the like back if it was there. Returns the new state.</summary>
    public async Task<FeedLikeView> ToggleLike(Session s, Guid postId, Guid userId)
    {
        var removed = await s.Exec("delete from feed_likes where post_id = @postId and user_id = @userId", new { postId, userId });
        if (removed == 0) await s.Exec("insert into feed_likes (post_id, user_id) values (@postId, @userId) on conflict do nothing", new { postId, userId });
        var count = await s.Scalar<int>("select like_count from feed_posts where id = @postId", new { postId });
        return new FeedLikeView { LikeCount = count, LikedByMe = removed == 0 };
    }

    public Task<FeedCommentRow> InsertComment(Session s, Guid postId, Guid authorId, string body) =>
        s.First<FeedCommentRow>(
            $"""
             with c as (insert into feed_comments (post_id, author_id, body) values (@postId, @authorId, @body) returning *)
             select c.id, c.post_id, c.author_id, {Name} as author_name, c.body, c.created_at from c join app_users u on u.id = c.author_id
             """, new { postId, authorId, body })!;

    public Task<Guid?> CommentAuthor(Session s, Guid me, bool admin, Guid id) =>
        s.First<Guid?>($"select c.author_id from feed_comments c join feed_posts p on p.id = c.post_id where c.id = @id and {Visible}", new { me, admin, id });

    public Task DeleteComment(Session s, Guid id) => s.Exec("delete from feed_comments where id = @id", new { id });
}
