using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Builds the full view of one submission: items (without file bytes) and the review history.</summary>
static class SubmissionAssembler
{
    public static async Task<SubmissionView> Build(Session s, SubmissionRepository repo, SubmissionRow row)
    {
        var items = await repo.Items(s, row.Id);
        var reviews = await repo.Reviews(s, row.Id);
        return new SubmissionView
        {
            Id = row.Id, TaskKey = row.TaskKey, Status = row.Status, Note = row.Note, Learned = row.Learned, Rating = row.Rating, Author = await repo.AuthorName(s, row.UserId),
            SubmittedAt = row.SubmittedAt, ReviewedAt = row.ReviewedAt, Editable = row.IsEditable,
            Items = items.Select(SubmissionItemView.From).ToList(), Reviews = reviews,
        };
    }
}

/// <summary>A person's own submissions: write notes, attach proof, submit. Everything is scoped to the signed-in person.</summary>
public sealed class SubmissionService(Db db, SubmissionRepository repo, AuditRepository audit)
{
    public const int MaxItems = 8;
    public const int MaxSubmissions = 60;
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const long MaxStorageBytes = 20L * 1024 * 1024;

    public Task<List<SubmissionListView>> Mine(Guid userId) => db.Read(s => repo.ListMine(s, userId));

    public Task<SubmissionView> Get(Guid userId, string taskKey) => db.Read(async s =>
    {
        var row = await repo.FindMine(s, userId, taskKey) ?? throw ApiException.NotFound();
        return await SubmissionAssembler.Build(s, repo, row);
    });

    public Task<SubmissionView> SaveDetails(AppUser user, string taskKey, SaveDetailsRequest details) => db.Tx(async s =>
    {
        var row = await EditableOrCreate(s, user.Id, taskKey);
        await repo.SetDetails(s, row.Id, details);
        await audit.Record(s, user.Id, "submission.details", row.Id.ToString());
        return await SubmissionAssembler.Build(s, repo, (await repo.ById(s, row.Id))!);
    });

    public Task<SubmissionView> AddLink(AppUser user, string taskKey, string label, string url) => db.Tx(async s =>
    {
        var row = await EditableOrCreate(s, user.Id, taskKey);
        await EnsureRoom(s, row.Id);
        await repo.AddLink(s, row.Id, label, url);
        await audit.Record(s, user.Id, "submission.add_link", row.Id.ToString());
        return await SubmissionAssembler.Build(s, repo, row);
    });

    /// <summary>The type is decided from the file's own bytes; the name and the type the browser claims are not trusted.</summary>
    public async Task<SubmissionView> AddFile(AppUser user, string taskKey, string? label, string? originalName, byte[] data)
    {
        if (data.Length == 0) throw ApiException.BadRequest("The file is empty");
        if (data.Length > MaxFileBytes) throw new ApiException(413, "Files can be up to 2 MB");
        var kind = FileSniffer.Detect(data) ?? throw new ApiException(415, "This file type is not accepted. Use PNG, JPG, GIF, WebP, PDF, text or ZIP.");
        var name = FileSniffer.SafeName(originalName, kind);
        var shownLabel = string.IsNullOrWhiteSpace(label) ? name : label.Trim();
        if (shownLabel.Length > 120) shownLabel = shownLabel[..120];

        return await db.Tx(async s =>
        {
            var row = await EditableOrCreate(s, user.Id, taskKey);
            await EnsureRoom(s, row.Id);
            if (await repo.StorageUsed(s, user.Id) + data.Length > MaxStorageBytes) throw new ApiException(413, "You have used all of your file storage (20 MB). Delete a file first.");
            await repo.AddFile(s, row.Id, shownLabel, name, kind.ContentType, data);
            await audit.Record(s, user.Id, "submission.add_file", row.Id.ToString(), new { kind.ContentType, size = data.Length });
            return await SubmissionAssembler.Build(s, repo, row);
        });
    }

    public Task<SubmissionView> DeleteItem(AppUser user, string taskKey, Guid itemId) => db.Tx(async s =>
    {
        var row = await EditableOrCreate(s, user.Id, taskKey, createIfMissing: false);
        if (!await repo.DeleteItem(s, row.Id, itemId)) throw ApiException.NotFound();
        await audit.Record(s, user.Id, "submission.delete_item", row.Id.ToString());
        return await SubmissionAssembler.Build(s, repo, row);
    });

    public Task<SubmissionView> Submit(AppUser user, string taskKey) => db.Tx(async s =>
    {
        var row = await EditableOrCreate(s, user.Id, taskKey, createIfMissing: false);
        var missing = new List<string>();
        if (await repo.ItemCount(s, row.Id) == 0) missing.Add("add at least one proof (a link or a file)");
        if (string.IsNullOrWhiteSpace(row.Learned)) missing.Add("write what you learned");
        if (row.Rating is null) missing.Add("rate yourself from 1 to 5");
        if (missing.Count > 0) throw ApiException.BadRequest("Before submitting, " + string.Join(", ", missing));
        await repo.SetStatus(s, row.Id, "submitted");
        await audit.Record(s, user.Id, "submission.submit", row.Id.ToString());
        return await SubmissionAssembler.Build(s, repo, (await repo.ById(s, row.Id))!);
    });

    /// <summary>The owner takes back a submission that is still waiting, to edit it. Once a reviewer has decided, it cannot be withdrawn.</summary>
    public Task<SubmissionView> Withdraw(AppUser user, string taskKey) => db.Tx(async s =>
    {
        await repo.LockUser(s, user.Id);
        var row = await repo.LockMine(s, user.Id, taskKey) ?? throw ApiException.NotFound();
        if (row.Status != "submitted") throw new ApiException(409, "Only a submission that is waiting for review can be withdrawn");
        await repo.SetStatus(s, row.Id, "draft");
        await audit.Record(s, user.Id, "submission.withdraw", row.Id.ToString());
        return await SubmissionAssembler.Build(s, repo, (await repo.ById(s, row.Id))!);
    });

    /// <summary>A file can be downloaded by its owner and by reviewers and admins. Anyone else is told it does not exist.</summary>
    public Task<StoredFile> Download(AppUser user, Guid itemId) => db.Read(async s =>
    {
        var file = await repo.File(s, itemId);
        // Owner: always. Reviewers and admins: only once the work is submitted (a draft is private). Anyone else: it does not exist.
        if (file is null || (file.OwnerId != user.Id && (user.Role == "member" || file.Status == "draft"))) throw ApiException.NotFound();
        return file;
    });

    async Task<SubmissionRow> EditableOrCreate(Session s, Guid userId, string taskKey, bool createIfMissing = true)
    {
        await repo.LockUser(s, userId);
        var row = await repo.LockMine(s, userId, taskKey);
        if (row is null)
        {
            if (!createIfMissing) throw ApiException.NotFound();
            if (await repo.CountForUser(s, userId) >= MaxSubmissions) throw new ApiException(429, "You have reached the limit of submissions");
            row = await repo.Create(s, userId, taskKey);
        }
        if (!row.IsEditable) throw new ApiException(409, "This submission cannot be changed while it waits for review or after it is approved");
        return row;
    }

    async Task EnsureRoom(Session s, Guid submissionId)
    {
        if (await repo.ItemCount(s, submissionId) >= MaxItems) throw new ApiException(429, $"A submission can hold up to {MaxItems} proofs");
    }
}

/// <summary>What reviewers and admins can do: see everyone's submitted work and decide on it.</summary>
public sealed class SubmissionReviewService(Db db, SubmissionRepository repo, AuditRepository audit)
{
    static readonly string[] Statuses = ["submitted", "changes_requested", "approved"];

    public Task<List<SubmissionListView>> List(string? status)
    {
        if (status is not null && status != "all" && !Statuses.Contains(status)) throw ApiException.BadRequest("Unknown status");
        return db.Read(s => repo.ListForReview(s, status is null or "all" ? null : status));
    }

    public Task<SubmissionCountsView> Counts() => db.Read(repo.Counts);

    /// <summary>Drafts are private to their owner, so a reviewer gets "not found" for them.</summary>
    public Task<SubmissionView> Detail(Guid id) => db.Read(async s =>
    {
        var row = await repo.ById(s, id);
        if (row is null || row.Status == "draft") throw ApiException.NotFound();
        return await SubmissionAssembler.Build(s, repo, row);
    });

    public Task<SubmissionView> Decide(AppUser reviewer, Guid id, string decision, string? comment) => db.Tx(async s =>
    {
        if (decision == "request_changes" && comment is null) throw ApiException.BadRequest("Say what needs to change when you ask for changes");
        var row = await repo.LockById(s, id);
        if (row is null || row.Status == "draft") throw ApiException.NotFound();
        if (row.Status != "submitted") throw new ApiException(409, "This submission is not waiting for review");
        var own = row.UserId == reviewer.Id;
        if (own && !reviewer.IsAdmin) throw new ApiException(403, "You cannot review your own submission");
        await repo.SetStatus(s, id, decision == "approve" ? "approved" : "changes_requested");
        await repo.AddReview(s, id, reviewer.Id, decision, comment);
        await audit.Record(s, reviewer.Id, "submission.review." + decision, id.ToString(), own ? new { self_review = true } : null);
        return await SubmissionAssembler.Build(s, repo, (await repo.ById(s, id))!);
    });
}
