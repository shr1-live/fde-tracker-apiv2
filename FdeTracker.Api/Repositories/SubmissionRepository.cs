using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class SubmissionRepository
{
    const string ItemCols = "id, submission_id, kind, label, url, file_name, content_type, size_bytes, created_at";
    const string Author = "coalesce(u.display_name, split_part(u.email, '@', 1))";

    /// <summary>Serialises a person's changes so the submission count and the storage total cannot be raced by parallel requests.</summary>
    public Task LockUser(Session s, Guid userId) => s.Exec("select pg_advisory_xact_lock(hashtextextended(@key, 0))", new { key = "submissions:" + userId });

    public Task<SubmissionRow?> FindMine(Session s, Guid userId, string taskKey) =>
        s.First<SubmissionRow>("select * from submissions where user_id = @userId and task_key = @taskKey", new { userId, taskKey });

    public Task<SubmissionRow?> LockMine(Session s, Guid userId, string taskKey) =>
        s.First<SubmissionRow>("select * from submissions where user_id = @userId and task_key = @taskKey for update", new { userId, taskKey });

    public Task<SubmissionRow?> LockById(Session s, Guid id) =>
        s.First<SubmissionRow>("select * from submissions where id = @id for update", new { id });

    public Task<SubmissionRow?> ById(Session s, Guid id) => s.First<SubmissionRow>("select * from submissions where id = @id", new { id });

    public Task<int> CountForUser(Session s, Guid userId) => s.Scalar<int>("select count(*)::int from submissions where user_id = @userId", new { userId });

    public Task<SubmissionRow> Create(Session s, Guid userId, string taskKey) =>
        s.First<SubmissionRow>("insert into submissions (user_id, task_key) values (@userId, @taskKey) returning *", new { userId, taskKey })!;

    public Task SetDetails(Session s, Guid id, SaveDetailsRequest d) =>
        s.Exec(
            """
            update submissions set
              note = case when @setNote then @note else note end,
              learned = case when @setLearned then @learned else learned end,
              rating = case when @setRating then @rating else rating end,
              updated_at = now()
            where id = @id
            """,
            new { id, note = d.Note, learned = d.Learned, rating = d.Rating, setNote = d.NoteSet, setLearned = d.LearnedSet, setRating = d.RatingSet });

    public Task SetStatus(Session s, Guid id, string status) =>
        s.Exec(
            """
            update submissions set status = @status, updated_at = now(),
              submitted_at = case when @status = 'submitted' then now() else submitted_at end,
              reviewed_at = case when @status in ('approved', 'changes_requested') then now() else reviewed_at end
            where id = @id
            """, new { id, status });

    public Task<List<SubmissionItemRow>> Items(Session s, Guid submissionId) =>
        s.Query<SubmissionItemRow>($"select {ItemCols} from submission_items where submission_id = @submissionId order by created_at", new { submissionId });

    public Task<int> ItemCount(Session s, Guid submissionId) => s.Scalar<int>("select count(*)::int from submission_items where submission_id = @submissionId", new { submissionId });

    public Task<long> StorageUsed(Session s, Guid userId) =>
        s.Scalar<long>("select coalesce(sum(i.size_bytes), 0)::bigint from submission_items i join submissions b on b.id = i.submission_id where b.user_id = @userId", new { userId });

    public Task AddLink(Session s, Guid submissionId, string label, string url) =>
        s.Exec("insert into submission_items (submission_id, kind, label, url) values (@submissionId, 'link', @label, @url)", new { submissionId, label, url });

    public Task AddFile(Session s, Guid submissionId, string label, string fileName, string contentType, byte[] data) =>
        s.Exec(
            "insert into submission_items (submission_id, kind, label, file_name, content_type, size_bytes, data) values (@submissionId, 'file', @label, @fileName, @contentType, @size, @data)",
            new { submissionId, label, fileName, contentType, size = data.Length, data });

    public async Task<bool> DeleteItem(Session s, Guid submissionId, Guid itemId) =>
        await s.Exec("delete from submission_items where id = @itemId and submission_id = @submissionId", new { itemId, submissionId }) > 0;

    public Task<StoredFile?> File(Session s, Guid itemId) =>
        s.First<StoredFile>(
            """
            select b.user_id as owner_id, b.status, i.file_name, i.content_type, i.data
            from submission_items i join submissions b on b.id = i.submission_id
            where i.id = @itemId and i.kind = 'file'
            """, new { itemId });

    public Task<List<SubmissionReviewView>> Reviews(Session s, Guid submissionId) =>
        s.Query<SubmissionReviewView>(
            $"""
            select r.decision, r.comment, {Author} as reviewer, r.created_at
            from submission_reviews r join app_users u on u.id = r.reviewer_id
            where r.submission_id = @submissionId order by r.created_at desc
            """, new { submissionId });

    public Task AddReview(Session s, Guid submissionId, Guid reviewerId, string decision, string? comment) =>
        s.Exec("insert into submission_reviews (submission_id, reviewer_id, decision, comment) values (@submissionId, @reviewerId, @decision, @comment)", new { submissionId, reviewerId, decision, comment });

    public Task<string> AuthorName(Session s, Guid userId) =>
        s.Scalar<string>($"select {Author} from app_users u where u.id = @userId", new { userId });

    public Task<List<SubmissionListView>> ListMine(Session s, Guid userId) =>
        s.Query<SubmissionListView>(
            $"""
            select b.id, b.task_key, b.status, {Author} as author, b.submitted_at, b.updated_at,
              (select count(*)::int from submission_items i where i.submission_id = b.id) as item_count,
              (select comment from submission_reviews r where r.submission_id = b.id order by r.created_at desc limit 1) as last_comment
            from submissions b join app_users u on u.id = b.user_id
            where b.user_id = @userId order by b.task_key
            """, new { userId });

    /// <summary>Everyone's submissions for reviewers. status null means all except drafts, which are private to their owner.</summary>
    public Task<List<SubmissionListView>> ListForReview(Session s, string? status) =>
        s.Query<SubmissionListView>(
            $"""
            select b.id, b.task_key, b.status, {Author} as author, b.submitted_at, b.updated_at,
              (select count(*)::int from submission_items i where i.submission_id = b.id) as item_count,
              (select comment from submission_reviews r where r.submission_id = b.id order by r.created_at desc limit 1) as last_comment
            from submissions b join app_users u on u.id = b.user_id
            where b.status <> 'draft' and (@status::text is null or b.status = @status)
            order by case b.status when 'submitted' then 0 when 'changes_requested' then 1 else 2 end, b.submitted_at nulls last, b.task_key
            limit 300
            """, new { status });

    public async Task<SubmissionCountsView> Counts(Session s)
    {
        var rows = await s.Query<(string Status, int N)>("select status, count(*)::int as n from submissions group by status");
        int Of(string st) => rows.FirstOrDefault(r => r.Status == st).N;
        return new SubmissionCountsView { Submitted = Of("submitted"), ChangesRequested = Of("changes_requested"), Approved = Of("approved"), Drafts = Of("draft") };
    }
}
