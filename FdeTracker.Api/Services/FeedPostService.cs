using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Each program's feed: posts, likes and comments seen only by that program's people and the admin. Limits are counted per person per UTC day.</summary>
public sealed class FeedPostService(Db db, FeedPostRepository repo, LearningProgramRepository programs)
{
    public const int PostsPerDay = 30;
    public const int CommentsPerDay = 200;
    public const int MaxLimit = 50;

    public const int ListComments = 20;
    public const int SingleComments = 50;
    public const int MaxSearch = 100;

    /// <summary>Checks the raw query values and turns them into a <see cref="FeedPostQuery"/>. Bad values are a 400.</summary>
    public static FeedPostQuery ParseQuery(string? sort, string? kind, bool mine, string? q, string? cursor)
    {
        var s = (sort ?? "new") switch
        {
            "new" => FeedSort.New,
            "top" => FeedSort.Top,
            "discussed" => FeedSort.Discussed,
            _ => throw ApiException.BadRequest("sort must be new, top or discussed"),
        };
        var k = (kind ?? "all") switch
        {
            "all" => null,
            "video" or "link" or "text" => kind,
            _ => throw ApiException.BadRequest("kind must be all, video, link or text"),
        };
        string? pattern = null;
        var text = q?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (text.Length > MaxSearch) throw ApiException.BadRequest($"q must be at most {MaxSearch} characters");
            // A literal search: the LIKE wildcards and the escape character itself are escaped.
            pattern = "%" + text.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
        }
        var after = string.IsNullOrEmpty(cursor) ? null : FeedCursor.Decode(cursor, s);
        return new FeedPostQuery(s, k, mine, pattern, after);
    }

    public Task<FeedPostPageView> Page(AppUser user, Guid? programId, FeedPostQuery query, int limit) => db.Read(async s =>
    {
        if (limit is < 1 or > MaxLimit) throw ApiException.BadRequest($"limit must be from 1 to {MaxLimit}");
        if (programId is { } pid) await LearningProgramService.Access(s, programs, user, pid); // 404 outside the program
        var rows = await repo.Page(s, user.Id, user.IsAdmin, programId, query, limit + 1);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        var posts = await WithComments(s, user, rows, ListComments);
        string? next = null;
        if (more)
        {
            var last = rows[^1];
            var count = query.Sort switch { FeedSort.Top => last.LikeCount, FeedSort.Discussed => last.CommentCount, _ => 0 };
            next = new FeedCursor(query.Sort, count, last.CreatedAt, last.Id).Encode();
        }
        return new FeedPostPageView { Items = posts, NextCursor = next };
    });

    public Task<FeedPostView> Get(AppUser user, Guid id) => db.Read(async s =>
    {
        var row = await repo.Post(s, user.Id, user.IsAdmin, id) ?? throw ApiException.NotFound();
        return (await WithComments(s, user, [row], SingleComments))[0];
    });

    /// <summary>Only the author may change a post (the admin can delete it, not rewrite it). Same rules as a new post.</summary>
    public Task<FeedPostView> Edit(AppUser user, Guid id, FeedPostPatchRequest b) => db.Tx(async s =>
    {
        var current = await repo.PostForEdit(s, id) ?? throw ApiException.NotFound();
        if (current.AuthorId != user.Id) throw new ApiException(403, "You can only edit your own posts");
        if (!b.BodyGiven && !b.UrlGiven) throw ApiException.BadRequest("Send body or url to change");
        var body = b.BodyGiven ? b.Body : current.Body;
        var url = b.UrlGiven ? b.Url : current.Url;
        if (body is null && url is null) throw ApiException.BadRequest("Write something or add a link");
        await repo.UpdatePost(s, id, body, url);
        var row = (await repo.Post(s, user.Id, user.IsAdmin, id))!;
        return (await WithComments(s, user, [row], ListComments))[0];
    });

    async Task<List<FeedPostView>> WithComments(Session s, AppUser user, List<FeedPostRow> rows, int perPost)
    {
        var comments = rows.Count == 0 ? [] : await repo.LastComments(s, rows.Select(p => p.Id).ToArray(), perPost);
        var byPost = comments.GroupBy(c => c.PostId).ToDictionary(g => g.Key, g => g.Select(Comment).ToList());
        return rows.Select(p => Post(p, byPost.GetValueOrDefault(p.Id) ?? [])).ToList();
    }

    public Task<FeedPostView> Create(AppUser user, FeedPostRequest b) => db.Tx(async s =>
    {
        if (b.Body is null && b.Url is null) throw ApiException.BadRequest("Write something or add a link");
        if (b.ProgramId is { } pid) await LearningProgramService.Access(s, programs, user, pid); // only into a program you are in
        await repo.LockAuthor(s, user.Id);
        if (await repo.PostsToday(s, user.Id) >= PostsPerDay) throw new ApiException(429, $"You can post up to {PostsPerDay} times a day");
        var id = await repo.InsertPost(s, user.Id, b.ProgramId, b.Body, b.Url);
        return Post((await repo.Post(s, user.Id, user.IsAdmin, id))!, []);
    });

    public Task Delete(AppUser user, Guid id) => db.Tx<bool>(async s =>
    {
        var author = await repo.PostAuthor(s, user.Id, user.IsAdmin, id) ?? throw ApiException.NotFound();
        if (author != user.Id && !user.IsAdmin) throw new ApiException(403, "You can only delete your own posts");
        await repo.DeletePost(s, id);
        return true;
    });

    public Task<FeedLikeView> ToggleLike(AppUser user, Guid id) => db.Tx(async s =>
    {
        if (await repo.PostAuthor(s, user.Id, user.IsAdmin, id) is null) throw ApiException.NotFound();
        return await repo.ToggleLike(s, id, user.Id);
    });

    public Task<FeedCommentView> AddComment(AppUser user, Guid postId, FeedCommentRequest b) => db.Tx(async s =>
    {
        if (await repo.PostAuthor(s, user.Id, user.IsAdmin, postId) is null) throw ApiException.NotFound();
        await repo.LockAuthor(s, user.Id);
        if (await repo.CommentsToday(s, user.Id) >= CommentsPerDay) throw new ApiException(429, $"You can comment up to {CommentsPerDay} times a day");
        return Comment(await repo.InsertComment(s, postId, user.Id, b.Body));
    });

    public Task DeleteComment(AppUser user, Guid commentId) => db.Tx<bool>(async s =>
    {
        var author = await repo.CommentAuthor(s, user.Id, user.IsAdmin, commentId) ?? throw ApiException.NotFound();
        if (author != user.Id && !user.IsAdmin) throw new ApiException(403, "You can only delete your own comments");
        await repo.DeleteComment(s, commentId);
        return true;
    });

    static FeedCommentView Comment(FeedCommentRow c) =>
        new() { Id = c.Id, Author = new FeedAuthorView { Id = c.AuthorId, Name = c.AuthorName }, Body = c.Body, CreatedAt = c.CreatedAt };

    static FeedPostView Post(FeedPostRow p, List<FeedCommentView> comments) => new()
    {
        Id = p.Id, Author = new FeedAuthorView { Id = p.AuthorId, Name = p.AuthorName }, Body = p.Body, Url = p.Url, CreatedAt = p.CreatedAt, EditedAt = p.EditedAt,
        LikeCount = p.LikeCount, LikedByMe = p.LikedByMe, CommentCount = p.CommentCount, Comments = comments,
    };
}
