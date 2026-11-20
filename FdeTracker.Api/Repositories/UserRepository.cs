using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

public sealed class UserRepository
{
    public Task<AppUser?> ById(Session s, Guid id) => s.First<AppUser>("select * from app_users where id = @id", new { id });
    public Task<AppUser?> ByEmail(Session s, string email) => s.First<AppUser>("select * from app_users where email = @email", new { email });

    public Task LockForSignup(Session s) => s.Exec("select pg_advisory_xact_lock(7001)");

    public Task<AppUser> Approve(Session s, Guid id) =>
        s.First<AppUser>("update app_users set access_status = 'approved' where id = @id returning *", new { id })!;

    public Task<AppUser> Insert(Session s, Guid id, string email, string role, string status) =>
        s.First<AppUser>("insert into app_users (id, email, role, access_status) values (@id, @email, @role, @status) returning *", new { id, email, role, status })!;

    public Task<AppUser> Relink(Session s, Guid newId, string email) =>
        s.First<AppUser>("update app_users set id = @newId, last_seen_at = now() where email = @email returning *", new { newId, email })!;

    public Task Touch(Session s, Guid id) => s.Exec("update app_users set last_seen_at = now() where id = @id", new { id });

    public Task<AppUser> MakeAdmin(Session s, Guid id) =>
        s.First<AppUser>("update app_users set role = 'admin', access_status = 'approved' where id = @id returning *", new { id })!;

    public Task<ProfileView?> UpdateProfile(Session s, Guid id, string? displayName, bool stackSet, string? stackId) =>
        s.First<ProfileView>(
            "update app_users set display_name = coalesce(@displayName, display_name), stack_id = case when @stackSet then @stackId else stack_id end where id = @id returning id, email, display_name, stack_id, role",
            new { id, displayName, stackSet, stackId });

    public Task<List<AdminUserView>> List(Session s) =>
        s.Query<AdminUserView>(
            """
            select u.id, u.email, u.display_name, u.stack_id, u.role, u.access_status, u.created_at, u.last_seen_at, u.batch_id, b.name as batch_name
            from app_users u left join batches b on b.id = u.batch_id order by u.created_at desc limit 500
            """);

    public Task<BatchAssignView?> SetBatch(Session s, Guid id, Guid? batchId) =>
        s.First<BatchAssignView>("update app_users set batch_id = @batchId where id = @id returning id, email, batch_id", new { id, batchId });

    public Task<AccessChangeView?> SetAccess(Session s, Guid id, string status) =>
        s.First<AccessChangeView>("update app_users set access_status = @status where id = @id and role <> 'admin' returning id, email, access_status", new { id, status });

    /// <summary>Sets member or reviewer for anyone except the owner (ADMIN_EMAIL) account. Null when there is no such person, or it is the owner.</summary>
    public Task<RoleChangeView?> SetRole(Session s, Guid id, string role, string adminEmail) =>
        s.First<RoleChangeView>("update app_users set role = @role where id = @id and email <> @adminEmail returning id, email, role", new { id, role, adminEmail });
}
