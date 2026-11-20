using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Batches: groups of learners who start together. The admin creates them; admin and reviewers can read them.</summary>
public sealed class BatchService(Db db, BatchRepository batches, AuditRepository audit)
{
    public Task<List<BatchListItemView>> List() => db.Read(batches.List);

    public async Task<BatchView> Create(Guid actorId, CreateBatchRequest b)
    {
        var row = await db.Tx(async s =>
        {
            var r = await batches.Insert(s, b.Name, b.StartsOnDate());
            if (r is not null) await audit.Record(s, actorId, "admin.batch_create", r.Id.ToString(), new { name = r.Name, starts_on = r.StartsOn });
            return r;
        });
        return row ?? throw new ApiException(409, "A batch with that name already exists");
    }
}
