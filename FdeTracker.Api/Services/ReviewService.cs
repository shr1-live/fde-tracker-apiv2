using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

public sealed class ReviewService(Db db, ReviewRepository reviews, AuditRepository audit)
{
    static readonly Dictionary<string, string> StatusFor = new() { ["approve"] = "approved", ["request_changes"] = "changes_requested", ["reject"] = "rejected" };

    public Task<List<ReviewQueueItemView>> Queue() => db.Read(reviews.Queue);

    public async Task<ReviewResultView> Decide(AppUser reviewer, Guid id, ReviewDecisionRequest b)
    {
        if (b.Decision != "approve" && b.Comment is null) throw ApiException.BadRequest("A comment is required when you do not approve");
        var status = await db.Tx(async s =>
        {
            var cur = await reviews.LockTarget(s, id) ?? throw ApiException.NotFound();
            if (cur.Status != "in_review") throw new ApiException(409, "This item is not waiting for review");
            var own = cur.AuthorId == reviewer.Id;
            if (own && !reviewer.IsAdmin) throw new ApiException(403, "You cannot review your own submission");
            var next = StatusFor[b.Decision];
            await reviews.SetStatus(s, id, next);
            await reviews.AddReview(s, id, reviewer.Id, b.Decision, b.Comment);
            await audit.Record(s, reviewer.Id, "review." + b.Decision, id.ToString(), own ? new { self_review = true } : null);
            return next;
        });
        return new ReviewResultView { Status = status };
    }
}
