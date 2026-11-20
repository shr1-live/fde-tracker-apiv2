using System.Globalization;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Who someone is inside one program: "admin" (the admin account, in every program), "coordinator" or "member".</summary>
public sealed record ProgramAccess(LpProgramRow Program, string Role)
{
    public bool CanManage => Role is "admin" or "coordinator";
    public DateOnly? StartsOn => Program.StartsOn is { } s ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}

/// <summary>Programs V3: programs, their plan (days), forms, members and invites. Every call checks the person's role in that program.</summary>
public sealed class LearningProgramService(Db db, LearningProgramRepository repo, UserRepository users, AuditRepository audit, AppOptions options)
{
    /// <summary>The default forms every new program starts with (the same as the seeded FDE program).</summary>
    public static readonly IReadOnlyList<LpFieldView> DefaultDaily =
    [
        new() { Key = "project", Label = "Project worked", Description = "Project name and hours spent. Write None if you are not on a project.", Type = "text", Required = true },
        new() { Key = "theory", Label = "Training theory", Description = "Topics covered, completion status and hours spent.", Type = "longtext", Required = true },
        new() { Key = "practical", Label = "Practical", Description = "Practical task progress or status.", Type = "longtext", Required = true },
    ];

    public static readonly IReadOnlyList<LpFieldView> DefaultDay =
    [
        new() { Key = "learned", Label = "What You Learn", Description = "What you learned from this day's topics, in your own words.", Type = "longtext", Required = true },
        new() { Key = "proof", Label = "Proof of Evidence", Description = "Links (GitHub, document, demo) or a short note that shows the work.", Type = "longtext", Required = false },
        new() { Key = "rating", Label = "Rate (1-5)", Description = Rubric.Text, Type = "rating", Required = true },
    ];

    // ---- access

    /// <summary>The person's access to a program. A program they are not in looks like it does not exist (404).</summary>
    public static async Task<ProgramAccess> Access(Session s, LearningProgramRepository repo, AppUser user, Guid programId)
    {
        var p = await repo.Program(s, programId) ?? throw ApiException.NotFound();
        if (user.IsAdmin) return new ProgramAccess(p, "admin");
        var role = await repo.RoleIn(s, programId, user.Id) ?? throw ApiException.NotFound();
        return new ProgramAccess(p, role);
    }

    /// <summary>Coordinator of this program, or the admin; otherwise 403 (or 404 for someone outside the program).</summary>
    public static async Task<ProgramAccess> Manager(Session s, LearningProgramRepository repo, AppUser user, Guid programId)
    {
        var a = await Access(s, repo, user, programId);
        return a.CanManage ? a : throw new ApiException(403, "Only a coordinator of this program can do that");
    }

    // ---- programs

    public Task<List<LpProgramSummaryView>> List(AppUser user) => db.Read(async s =>
        user.IsAdmin
            ? (await repo.AllPrograms(s)).Select(p => Summary(p, "admin")).ToList()
            : (await repo.MyPrograms(s, user.Id)).Select(p => Summary(p, p.Role ?? "member")).ToList());

    static LpProgramSummaryView Summary(LpProgramRow p, string role) =>
        new() { Id = p.Id, Name = p.Name, Description = p.Description, StartsOn = p.StartsOn, MyRole = role, MemberCount = p.MemberCount };

    public Task<LpProgramSummaryView> Create(AppUser user, CreateLpProgramRequest b) => db.Tx(async s =>
    {
        if (!user.IsAdmin) throw new ApiException(403, "Only the admin can create programs");
        var id = await repo.InsertProgram(s, b.Name, b.Description, b.StartsOn) ?? throw new ApiException(409, "A program with that name already exists");
        await repo.ReplaceForm(s, id, "daily", DefaultDaily);
        await repo.ReplaceForm(s, id, "day", DefaultDay);
        await audit.Record(s, user.Id, "program.create", id.ToString(), new { name = b.Name });
        return Summary((await repo.Program(s, id))!, "admin");
    });

    public Task<LpProgramSummaryView> Update(AppUser user, Guid id, PatchLpProgramRequest b) => db.Tx(async s =>
    {
        var a = await Manager(s, repo, user, id);
        if (b.NameSet && b.Name is { } name && await repo.NameTaken(s, name, id)) throw new ApiException(409, "A program with that name already exists");
        await repo.UpdateProgram(s, id, b.NameSet ? b.Name : null, b.DescriptionSet, b.Description, b.StartsOnSet, b.StartsOn);
        await audit.Record(s, user.Id, "program.update", id.ToString(), new { });
        return Summary((await repo.Program(s, id))!, a.Role);
    });

    public Task<LpProgramDetailView> Detail(AppUser user, Guid id) => db.Read(async s =>
    {
        var a = await Access(s, repo, user, id);
        var fields = await repo.Fields(s, id);
        return new LpProgramDetailView
        {
            Id = a.Program.Id, Name = a.Program.Name, Description = a.Program.Description, StartsOn = a.Program.StartsOn, MyRole = a.Role,
            Days = await repo.Days(s, id),
            Forms = new LpFormsView { Daily = fields.Where(f => f.Form == "daily").ToList(), Day = fields.Where(f => f.Form == "day").ToList() },
        };
    });

    // ---- days

    public Task<LpDayView> AddDay(AppUser user, Guid id, CreateLpDayRequest b) => db.Tx(async s =>
    {
        await Manager(s, repo, user, id);
        return await repo.InsertDay(s, id, b.Week, b.Day, b.Topic, b.TopicsToCover, b.PracticalTask, b.Resources)
            ?? throw new ApiException(409, $"Week {b.Week} day {b.Day} already exists");
    });

    public Task<LpDayView> UpdateDay(AppUser user, Guid id, Guid dayId, PatchLpDayRequest b) => db.Tx(async s =>
    {
        await Manager(s, repo, user, id);
        var d = await repo.Day(s, id, dayId) ?? throw ApiException.NotFound();
        var next = new LpDayView
        {
            Id = d.Id, Week = b.Week ?? d.Week, Day = b.Day ?? d.Day,
            Topic = b.TopicSet ? b.Topic! : d.Topic,
            TopicsToCover = b.CoverSet ? b.TopicsToCover ?? "" : d.TopicsToCover,
            PracticalTask = b.PracticalSet ? b.PracticalTask : d.PracticalTask,
            Resources = b.ResourcesSet ? b.Resources : d.Resources,
        };
        if (await repo.DaySlotTaken(s, id, next.Week, next.Day, dayId)) throw new ApiException(409, $"Week {next.Week} day {next.Day} already exists");
        return (await repo.UpdateDay(s, id, dayId, next))!;
    });

    public Task DeleteDay(AppUser user, Guid id, Guid dayId) => db.Tx<bool>(async s =>
    {
        await Manager(s, repo, user, id);
        if (!await repo.DeleteDay(s, id, dayId)) throw ApiException.NotFound();
        await audit.Record(s, user.Id, "program.day_delete", dayId.ToString(), new { program_id = id });
        return true;
    });

    // ---- forms

    public Task<List<LpFieldView>> SaveForm(AppUser user, Guid id, string form, SaveLpFormRequest b) => db.Tx(async s =>
    {
        if (form is not ("daily" or "day")) throw ApiException.NotFound();
        await Manager(s, repo, user, id);
        var fields = b.Fields.Select(f => new LpFieldView { Key = f.Key, Label = f.Label, Description = f.Description, Type = f.Type, Required = f.Required, Form = form }).ToList();
        await repo.ReplaceForm(s, id, form, fields);
        await audit.Record(s, user.Id, "program.form", id.ToString(), new { form, count = fields.Count });
        return fields;
    });

    // ---- members and invites

    public Task<LpMembersView> Members(AppUser user, Guid id) => db.Read(async s =>
    {
        await Manager(s, repo, user, id);
        return new LpMembersView { Members = await repo.Members(s, id), Invites = await repo.OpenInvites(s, id) };
    });

    /// <summary>Someone who has signed in before is added at once (and approved); anyone else gets an invite that is used when they sign in.</summary>
    public Task<LpInviteResultView> Invite(AppUser user, Guid id, LpInviteRequest b) => db.Tx(async s =>
    {
        await Manager(s, repo, user, id);
        if (!b.Email.EndsWith("@" + options.AllowedDomain, StringComparison.Ordinal)) throw ApiException.BadRequest($"Only @{options.AllowedDomain} accounts can sign in");
        if (b.Email == options.AdminEmail) throw ApiException.BadRequest("The admin is already in every program");
        await users.LockForSignup(s); // the same lock sign-in takes, so an invite and a first sign-in cannot miss each other
        var existing = await users.ByEmail(s, b.Email);
        if (existing is not null)
        {
            await repo.AddMember(s, id, existing.Id, b.Role);
            await repo.CloseInvite(s, id, b.Email);
            // Only "pending" becomes approved: a coordinator must never undo the admin's "rejected".
            if (existing.AccessStatus == "pending") await users.SetAccess(s, existing.Id, "approved");
            await audit.Record(s, user.Id, "program.member_add", existing.Id.ToString(), new { program_id = id, role = b.Role });
            return new LpInviteResultView { Status = "added" };
        }
        await repo.UpsertInvite(s, id, b.Email, b.Role, user.Id);
        await audit.Record(s, user.Id, "program.invite", id.ToString(), new { role = b.Role });
        return new LpInviteResultView { Status = "invited" };
    });

    public Task CancelInvite(AppUser user, Guid id, Guid inviteId) => db.Tx<bool>(async s =>
    {
        await Manager(s, repo, user, id);
        if (!await repo.DeleteInvite(s, id, inviteId)) throw ApiException.NotFound();
        return true;
    });

    public Task<LpMemberView> SetRole(AppUser user, Guid id, Guid userId, string role) => db.Tx(async s =>
    {
        await Manager(s, repo, user, id);
        if (userId == user.Id) throw ApiException.BadRequest("You cannot change your own role");
        if (await repo.SetMemberRole(s, id, userId, role) == 0) throw ApiException.NotFound();
        await audit.Record(s, user.Id, "program.member_role", userId.ToString(), new { program_id = id, role });
        return (await repo.Member(s, id, userId))!;
    });

    public Task RemoveMember(AppUser user, Guid id, Guid userId) => db.Tx<bool>(async s =>
    {
        await Manager(s, repo, user, id);
        if (userId == user.Id) throw ApiException.BadRequest("You cannot remove yourself");
        if (await users.ById(s, userId) is { } target && target.Email == options.AdminEmail) throw ApiException.BadRequest("The admin account cannot be removed");
        if (!await repo.RemoveMember(s, id, userId)) throw ApiException.NotFound();
        await audit.Record(s, user.Id, "program.member_remove", userId.ToString(), new { program_id = id });
        return true;
    });
}

/// <summary>The 1-5 readiness rubric (the "Readiness Guide").</summary>
public static class Rubric
{
    public static readonly IReadOnlyList<string> Lines =
    [
        "Needs significant guidance; cannot yet complete the task independently.",
        "Understands some concepts but needs frequent help to implement.",
        "Can implement the task with some guidance and review.",
        "Can independently implement, test, and troubleshoot the task.",
        "Can independently deliver and validate a customer-ready solution, explain trade-offs, and handle realistic edge cases.",
    ];

    public static readonly string Text = string.Join("\n", Lines.Select((l, i) => $"{i + 1} = {l}"));
}
