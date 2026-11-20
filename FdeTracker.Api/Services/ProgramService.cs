using System.Text.RegularExpressions;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

public sealed partial class ProgramService(Db db, ProgramRepository programs)
{
    const int MaxPrograms = 5;
    const int MaxItems = 60;

    public Task<List<ProgramSummaryView>> List(Guid userId) => db.Read(s => programs.List(s, userId));

    public Task<ProgramDetailView> Detail(Guid userId, string slug) => db.Read(async s =>
    {
        var p = await programs.Visible(s, slug, userId) ?? throw ApiException.NotFound();
        var items = await programs.Items(s, p.Id, userId);
        return new ProgramDetailView { Slug = p.Slug, Title = p.Title, Description = p.Description, Visibility = p.Visibility, Mine = p.OwnerId == userId, Items = items };
    });

    public Task<ProgramCreatedView> Create(Guid userId, CreateProgramRequest b) => db.Tx(async s =>
    {
        if (await programs.OwnedCount(s, userId) >= MaxPrograms) throw new ApiException(429, $"You can have up to {MaxPrograms} plans");
        var baseSlug = NonSlugChars().Replace(b.Title.ToLowerInvariant(), "-").Trim('-');
        baseSlug = baseSlug.Length > 40 ? baseSlug[..40].Trim('-') : baseSlug;
        if (baseSlug.Length == 0) baseSlug = "plan";
        var slug = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
        return await programs.Insert(s, slug, b.Title, b.Description, userId);
    });

    public Task<NewItemView> AddItem(Guid userId, string slug, AddItemRequest b) => db.Tx(async s =>
    {
        var p = await Owned(s, slug, userId);
        var (count, next) = await programs.ItemStats(s, p.Id);
        if (count >= MaxItems) throw new ApiException(429, $"A plan can have up to {MaxItems} steps");
        return await programs.InsertItem(s, p.Id, next, b.Title, b.Description);
    });

    public Task RemoveItem(Guid userId, string slug, Guid itemId) => db.Tx<bool>(async s =>
    {
        var p = await Owned(s, slug, userId);
        if (!await programs.DeleteItem(s, itemId, p.Id)) throw ApiException.NotFound();
        return true;
    });

    public Task<ProgressView> SetProgress(Guid userId, string slug, Guid itemId, bool done) => db.Tx(async s =>
    {
        var p = await programs.Visible(s, slug, userId) ?? throw ApiException.NotFound();
        if (await programs.ItemExists(s, itemId, p.Id) is null) throw ApiException.NotFound();
        if (done) await programs.MarkDone(s, userId, itemId); else await programs.MarkNotDone(s, userId, itemId);
        return new ProgressView { Done = done };
    });

    /// <summary>A plan the user may edit. Shared plans and other people's plans look like they do not exist.</summary>
    async Task<ProgramRecord> Owned(Session s, string slug, Guid userId)
    {
        var p = await programs.Visible(s, slug, userId);
        return p is { } && p.OwnerId == userId ? p : throw ApiException.NotFound();
    }

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonSlugChars();
}
