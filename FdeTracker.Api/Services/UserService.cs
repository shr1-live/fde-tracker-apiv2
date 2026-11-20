using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

public sealed class UserService(Db db, UserRepository users, BatchRepository batches, AuditRepository audit, LearningProgramRepository programs, AppOptions options)
{
    /// <summary>Loads the app user for a verified token, creating (or re-linking) them the first time we see them.
    /// Invite-only (Programs V3): the admin email is always admin; anyone else is approved only through an invite (or an earlier approval).
    /// Every open invite for the email becomes a membership on each sign-in.</summary>
    public Task<AppUser> SignIn(Guid id, string email) => db.Tx(async s =>
    {
        await users.LockForSignup(s);
        var isAdmin = email == options.AdminEmail;
        var user = await users.ById(s, id);
        if (user is null)
        {
            // The same person re-created in the auth provider gets a new id; keep their data by re-linking on email.
            if (await users.ByEmail(s, email) is not null)
            {
                user = await users.Relink(s, id, email);
            }
            else
            {
                user = await users.Insert(s, id, email, isAdmin ? "admin" : "member", isAdmin ? "approved" : "pending");
                await audit.Record(s, id, "user.first_seen", id.ToString(), new { status = user.AccessStatus });
            }
        }
        else
        {
            await users.Touch(s, id);
        }
        if (isAdmin && (user.Role != "admin" || user.AccessStatus != "approved")) user = await users.MakeAdmin(s, user.Id);
        if (!isAdmin && await programs.AcceptInvites(s, user.Id, email) > 0)
        {
            await audit.Record(s, user.Id, "user.invite_accepted", user.Id.ToString(), new { });
            if (user.AccessStatus == "pending") user = await users.Approve(s, user.Id); // never lifts an admin "rejected"
        }
        return user;
    });

    public async Task<ProfileView> UpdateProfile(Guid userId, UpdateMeRequest body) =>
        await db.Read(s => users.UpdateProfile(s, userId, body.DisplayName, body.StackIdSet, body.StackId)) ?? throw ApiException.NotFound();

    public Task<List<AdminUserView>> List() => db.Read(users.List);

    public async Task<AccessChangeView> SetAccess(Guid actorId, Guid id, string status)
    {
        if (id == actorId) throw ApiException.BadRequest("You cannot change your own access");
        var row = await db.Tx(async s =>
        {
            var r = await users.SetAccess(s, id, status);
            if (r is not null) await audit.Record(s, actorId, "admin.access", id.ToString(), new { status });
            return r;
        });
        return row ?? throw ApiException.NotFound();
    }

    /// <summary>The signed-in person as /api/me shows them, with their batch (or null).</summary>
    public async Task<UserView> Me(AppUser user)
    {
        return await db.Read(async s =>
        {
            var batch = user.BatchId is { } bid ? await batches.ById(s, bid) : null;
            List<MyProgramView> mine = !user.IsApproved ? []
                : user.IsAdmin ? (await programs.AllPrograms(s)).Select(p => new MyProgramView { Id = p.Id, Name = p.Name, Role = "admin" }).ToList()
                : (await programs.MyPrograms(s, user.Id)).Select(p => new MyProgramView { Id = p.Id, Name = p.Name, Role = p.Role ?? "member" }).ToList();
            return UserView.From(user, batch, mine);
        });
    }

    public async Task<BatchAssignView> SetBatch(Guid actorId, Guid id, Guid? batchId)
    {
        var row = await db.Tx(async s =>
        {
            if (batchId is { } bid && await batches.ById(s, bid) is null) throw ApiException.NotFound("Unknown batch");
            var r = await users.SetBatch(s, id, batchId);
            if (r is not null) await audit.Record(s, actorId, "admin.batch", id.ToString(), new { batch_id = batchId });
            return r;
        });
        return row ?? throw ApiException.NotFound();
    }

    public async Task<RoleChangeView> SetRole(Guid actorId, Guid id, string role)
    {
        var row = await db.Tx(async s =>
        {
            var r = await users.SetRole(s, id, role, options.AdminEmail);
            if (r is not null) await audit.Record(s, actorId, "admin.role", id.ToString(), new { role });
            return r;
        });
        return row ?? throw ApiException.NotFound();
    }
}
