using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services.Lab;

/// <summary>
/// Saved practice-lab runs. A person sees and deletes only their own runs. An admin can pin one run per day as the shared sample
/// everyone can open. A pinned run can be deleted only by an admin who owns it; its owner otherwise gets a clear refusal and asks for it to be unpinned.
/// </summary>
public sealed partial class LabRunService(Db db, LabRunRepository runs)
{
    public const int MaxRunsPerPerson = 50;
    public const int MaxResultBytes = 64 * 1024;

    // A Gemini key pasted into a result would be shown to everyone if the run became a sample, so a result that holds one is refused.
    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{30,}|AQ\.[0-9A-Za-z_\-.]{20,}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex KeyLike();

    public async Task<LabRunView> Save(Guid userId, SaveLabRunRequest req)
    {
        var json = req.Result.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaxResultBytes) throw new ApiException(413, "The result is too large to save. The limit is 64 KB.");
        if (KeyLike().IsMatch(json) || KeyLike().IsMatch(req.Label) || req.Models.Any(m => KeyLike().IsMatch(m)))
            throw ApiException.BadRequest("The result seems to contain an API key. Remove it before saving.");
        var models = req.Models.Select(m => m.Trim()).ToArray();

        var row = await db.Tx(async s =>
        {
            await runs.LockUser(s, userId);
            if (await runs.CountForUser(s, userId) >= MaxRunsPerPerson)
                throw new ApiException(409, $"You already have {MaxRunsPerPerson} saved runs, the most allowed. Delete an old one to save this run.");
            return await runs.Insert(s, userId, req.Day, req.Label, models, json);
        });
        return Run(row);
    }

    public async Task<LabRunsView> Mine(Guid userId) => new() { Runs = (await db.Read(s => runs.Mine(s, userId, MaxRunsPerPerson))).Select(Run).ToList() };

    public async Task<LabSamplesView> Samples() => new() { Samples = (await db.Read(runs.Samples)).Select(Sample).ToList() };

    /// <summary>Your own run only: someone else's run answers 404, as if it did not exist.</summary>
    public Task Delete(AppUser user, Guid id) => db.Tx(async s =>
    {
        var row = await runs.ById(s, id, forUpdate: true);
        if (row is null || row.UserId != user.Id) throw ApiException.NotFound("Run not found");
        if (row.IsSample && !user.IsAdmin) throw new ApiException(409, "This run is the shared sample for its day. Ask an admin to unpin it before deleting it.");
        await runs.Delete(s, id, user.Id);
        return 0;
    });

    /// <summary>
    /// Admin only (checked by the controller), and only the admin's OWN run: someone else's private run is never published without them.
    /// Unpins the day's previous sample in the same transaction. Unpin works on any run, so a bad pin can always be removed.
    /// </summary>
    public async Task<LabRunView> Pin(AppUser admin, Guid id) => Run(await db.Tx(async s =>
    {
        await runs.LockSamples(s);
        var row = await runs.ById(s, id, forUpdate: true);
        if (row is null || row.UserId != admin.Id) throw ApiException.NotFound("Run not found");
        await runs.Pin(s, id, row.Day);
        row.IsSample = true;
        return row;
    }));

    public async Task<LabRunView> Unpin(Guid id) => Run(await db.Tx(async s =>
    {
        var row = await runs.ById(s, id, forUpdate: true) ?? throw ApiException.NotFound("Run not found");
        await runs.Unpin(s, id);
        row.IsSample = false;
        return row;
    }));

    static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    static LabRunView Run(LabRunRow r) => new() { Id = r.Id, Day = r.Day, Label = r.Label, Models = r.Models, Result = Parse(r.ResultJson), CreatedAt = r.CreatedAt, IsSample = r.IsSample };
    static LabSampleView Sample(LabRunRow r) => new() { Id = r.Id, Day = r.Day, Label = r.Label, Models = r.Models, Result = Parse(r.ResultJson), CreatedAt = r.CreatedAt };
}
