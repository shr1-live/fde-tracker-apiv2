using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class ProgramRepository
{
    public Task<ProgramRecord?> Visible(Session s, string slug, Guid userId) =>
        s.First<ProgramRecord>("select id, slug, title, description, visibility, owner_id from programs where slug = @slug and (visibility = 'public' or owner_id = @userId)", new { slug, userId });

    public Task<List<ProgramSummaryView>> List(Session s, Guid userId) =>
        s.Query<ProgramSummaryView>(
            """
            select p.slug, p.title, p.description, p.visibility,
              (select count(*)::int from program_items i where i.program_id = p.id) as total,
              (select count(*)::int from progress g join program_items i on i.id = g.program_item_id where i.program_id = p.id and g.user_id = @userId) as done
            from programs p where p.visibility = 'public' or p.owner_id = @userId order by p.visibility, p.created_at
            """, new { userId });

    public Task<List<ProgramItemView>> Items(Session s, Guid programId, Guid userId) =>
        s.Query<ProgramItemView>(
            """
            select i.id, i.position, i.title, i.description, i.content_id, (g.user_id is not null) as done
            from program_items i left join progress g on g.program_item_id = i.id and g.user_id = @userId where i.program_id = @programId order by i.position
            """, new { programId, userId });

    public Task<int> OwnedCount(Session s, Guid userId) => s.Scalar<int>("select count(*)::int from programs where owner_id = @userId", new { userId });

    public Task<ProgramCreatedView> Insert(Session s, string slug, string title, string? description, Guid ownerId) =>
        s.First<ProgramCreatedView>(
            "insert into programs (slug, title, description, visibility, owner_id) values (@slug, @title, @description, 'private', @ownerId) returning slug, title",
            new { slug, title, description, ownerId })!;

    public Task<(int Count, int Next)> ItemStats(Session s, Guid programId) =>
        s.First<(int Count, int Next)>("select count(*)::int as count, (coalesce(max(position), -1) + 1)::int as next from program_items where program_id = @programId", new { programId });

    public Task<NewItemView> InsertItem(Session s, Guid programId, int position, string title, string? description) =>
        s.First<NewItemView>(
            "insert into program_items (program_id, position, title, description) values (@programId, @position, @title, @description) returning id, position, title, description",
            new { programId, position, title, description })!;

    public async Task<bool> DeleteItem(Session s, Guid itemId, Guid programId) =>
        await s.Exec("delete from program_items where id = @itemId and program_id = @programId", new { itemId, programId }) > 0;

    public Task<Guid?> ItemExists(Session s, Guid itemId, Guid programId) =>
        s.First<Guid?>("select id from program_items where id = @itemId and program_id = @programId", new { itemId, programId });

    public Task MarkDone(Session s, Guid userId, Guid itemId) =>
        s.Exec("insert into progress (user_id, program_item_id) values (@userId, @itemId) on conflict do nothing", new { userId, itemId });

    public Task MarkNotDone(Session s, Guid userId, Guid itemId) =>
        s.Exec("delete from progress where user_id = @userId and program_item_id = @itemId", new { userId, itemId });
}
