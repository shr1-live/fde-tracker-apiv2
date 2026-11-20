using FdeTracker.Api.Data;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Repositories;

/// <summary>Programs V3 tables (lp_*). Parameterised SQL only. Dates go in and out as "YYYY-MM-DD" text.</summary>
public sealed class LearningProgramRepository
{
    /// <summary>A person's name: the display name or the part of the email before "@".</summary>
    public static string NameOf(string alias) => $"split_part(coalesce(nullif(btrim({alias}.display_name), ''), {alias}.email), '@', 1)";

    const string ProgramCols =
        "p.id, p.name, p.description, to_char(p.starts_on, 'YYYY-MM-DD') as starts_on, (select count(*)::int from lp_members m2 where m2.program_id = p.id) as member_count";

    const string DayCols = "d.id, d.week, d.day, d.topic, d.topics_to_cover, d.practical_task, d.resources";

    // ---- programs

    public Task<List<LpProgramRow>> AllPrograms(Session s) =>
        s.Query<LpProgramRow>($"select {ProgramCols} from lp_programs p order by p.created_at, p.name");

    /// <summary>The programs a person belongs to, with their role there (in <see cref="LpProgramRow.Role"/>).</summary>
    public Task<List<LpProgramRow>> MyPrograms(Session s, Guid userId) =>
        s.Query<LpProgramRow>(
            $"select {ProgramCols}, m.role from lp_programs p join lp_members m on m.program_id = p.id and m.user_id = @userId order by p.created_at, p.name",
            new { userId });

    public Task<LpProgramRow?> Program(Session s, Guid id) =>
        s.First<LpProgramRow>($"select {ProgramCols} from lp_programs p where p.id = @id", new { id });

    /// <summary>The person's role in a program ("coordinator" or "member"), or null.</summary>
    public Task<string?> RoleIn(Session s, Guid programId, Guid userId) =>
        s.First<string?>("select role from lp_members where program_id = @programId and user_id = @userId", new { programId, userId });

    /// <summary>Null when the name is already taken.</summary>
    public Task<Guid?> InsertProgram(Session s, string name, string? description, string? startsOn) =>
        s.First<Guid?>(
            "insert into lp_programs (name, description, starts_on) values (@name, @description, @startsOn::date) on conflict (name) do nothing returning id",
            new { name, description, startsOn });

    public Task<bool> NameTaken(Session s, string name, Guid exceptId) =>
        s.Scalar<bool>("select exists (select 1 from lp_programs where name = @name and id <> @exceptId)", new { name, exceptId });

    public Task<int> UpdateProgram(Session s, Guid id, string? name, bool descriptionSet, string? description, bool startsSet, string? startsOn) =>
        s.Exec(
            """
            update lp_programs set
              name = coalesce(@name, name),
              description = case when @descriptionSet then @description else description end,
              starts_on = case when @startsSet then @startsOn::date else starts_on end
            where id = @id
            """, new { id, name, descriptionSet, description, startsSet, startsOn });

    // ---- days

    public Task<List<LpDayView>> Days(Session s, Guid programId) =>
        s.Query<LpDayView>($"select {DayCols} from lp_days d where d.program_id = @programId order by d.week, d.day", new { programId });

    public Task<LpDayView?> Day(Session s, Guid programId, Guid dayId) =>
        s.First<LpDayView>($"select {DayCols} from lp_days d where d.program_id = @programId and d.id = @dayId", new { programId, dayId });

    public Task<bool> DaySlotTaken(Session s, Guid programId, int week, int day, Guid exceptId) =>
        s.Scalar<bool>("select exists (select 1 from lp_days where program_id = @programId and week = @week and day = @day and id <> @exceptId)",
            new { programId, week, day, exceptId });

    /// <summary>Null when that week and day already exist.</summary>
    public Task<LpDayView?> InsertDay(Session s, Guid programId, int week, int day, string topic, string topicsToCover, string? practicalTask, string? resources) =>
        s.First<LpDayView>(
            """
            insert into lp_days as d (program_id, week, day, topic, topics_to_cover, practical_task, resources)
            values (@programId, @week, @day, @topic, @topicsToCover, @practicalTask, @resources)
            on conflict (program_id, week, day) do nothing
            returning d.id, d.week, d.day, d.topic, d.topics_to_cover, d.practical_task, d.resources
            """, new { programId, week, day, topic, topicsToCover, practicalTask, resources });

    public Task<LpDayView?> UpdateDay(Session s, Guid programId, Guid dayId, LpDayView v) =>
        s.First<LpDayView>(
            """
            update lp_days as d set week = @Week, day = @Day, topic = @Topic, topics_to_cover = @TopicsToCover, practical_task = @PracticalTask, resources = @Resources
            where d.program_id = @programId and d.id = @dayId
            returning d.id, d.week, d.day, d.topic, d.topics_to_cover, d.practical_task, d.resources
            """, new { programId, dayId, v.Week, v.Day, v.Topic, v.TopicsToCover, v.PracticalTask, v.Resources });

    public async Task<bool> DeleteDay(Session s, Guid programId, Guid dayId) =>
        await s.Exec("delete from lp_days where program_id = @programId and id = @dayId", new { programId, dayId }) > 0;

    // ---- forms

    public Task<List<LpFieldView>> Fields(Session s, Guid programId) =>
        s.Query<LpFieldView>(
            "select form, key, label, description, type, required from lp_form_fields where program_id = @programId order by form, position, key",
            new { programId });

    public async Task ReplaceForm(Session s, Guid programId, string form, IReadOnlyList<LpFieldView> fields)
    {
        await s.Exec("delete from lp_form_fields where program_id = @programId and form = @form", new { programId, form });
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            await s.Exec(
                """
                insert into lp_form_fields (program_id, form, key, label, description, type, required, position)
                values (@programId, @form, @Key, @Label, @Description, @Type, @Required, @position)
                """, new { programId, form, f.Key, f.Label, f.Description, f.Type, f.Required, position = i });
        }
    }

    // ---- members and invites

    public Task<List<LpMemberView>> Members(Session s, Guid programId) =>
        s.Query<LpMemberView>(
            $"""
             select u.id as user_id, u.email, {NameOf("u")} as name, m.role
             from lp_members m join app_users u on u.id = m.user_id
             where m.program_id = @programId
             order by lower({NameOf("u")}), u.email
             """, new { programId });

    public Task<LpMemberView?> Member(Session s, Guid programId, Guid userId) =>
        s.First<LpMemberView>(
            $"""
             select u.id as user_id, u.email, {NameOf("u")} as name, m.role
             from lp_members m join app_users u on u.id = m.user_id
             where m.program_id = @programId and m.user_id = @userId
             """, new { programId, userId });

    public Task<List<LpInviteView>> OpenInvites(Session s, Guid programId) =>
        s.Query<LpInviteView>(
            "select id, email, role, created_at from lp_invites where program_id = @programId and accepted_at is null order by created_at, email",
            new { programId });

    public Task AddMember(Session s, Guid programId, Guid userId, string role) =>
        s.Exec(
            "insert into lp_members (program_id, user_id, role) values (@programId, @userId, @role) on conflict (program_id, user_id) do update set role = excluded.role",
            new { programId, userId, role });

    public Task<int> SetMemberRole(Session s, Guid programId, Guid userId, string role) =>
        s.Exec("update lp_members set role = @role where program_id = @programId and user_id = @userId", new { programId, userId, role });

    public async Task<bool> RemoveMember(Session s, Guid programId, Guid userId) =>
        await s.Exec("delete from lp_members where program_id = @programId and user_id = @userId", new { programId, userId }) > 0;

    /// <summary>Stores an open invite. Re-inviting the same email to the same program updates the role and opens it again.</summary>
    public Task UpsertInvite(Session s, Guid programId, string email, string role, Guid invitedBy) =>
        s.Exec(
            """
            insert into lp_invites (email, program_id, role, invited_by) values (@email, @programId, @role, @invitedBy)
            on conflict (email, program_id) do update set role = excluded.role, invited_by = excluded.invited_by, created_at = now(), accepted_at = null
            """, new { programId, email, role, invitedBy });

    /// <summary>Marks the email's invite to this program as used (when the person was added directly).</summary>
    public Task CloseInvite(Session s, Guid programId, string email) =>
        s.Exec("update lp_invites set accepted_at = now() where program_id = @programId and email = @email and accepted_at is null", new { programId, email });

    public async Task<bool> DeleteInvite(Session s, Guid programId, Guid inviteId) =>
        await s.Exec("delete from lp_invites where program_id = @programId and id = @inviteId and accepted_at is null", new { programId, inviteId }) > 0;

    /// <summary>Turns every open invite for this email into a membership. Returns how many were used.</summary>
    public Task<int> AcceptInvites(Session s, Guid userId, string email) =>
        s.Scalar<int>(
            """
            with inv as (
              update lp_invites set accepted_at = now() where email = @email and accepted_at is null returning program_id, role
            ), added as (
              insert into lp_members (program_id, user_id, role) select program_id, @userId, role from inv
              on conflict (program_id, user_id) do update set role = excluded.role
              returning 1
            )
            select count(*)::int from added
            """, new { userId, email });

    // ---- status

    public Task<List<LpDailyRow>> Daily(Session s, Guid programId, Guid? userId, string? from, string? to) =>
        s.Query<LpDailyRow>(
            """
            select x.user_id, to_char(x.on_date, 'YYYY-MM-DD') as on_date, x."values"::text as values_json, x.updated_at
            from lp_daily_status x
            join lp_members m on m.program_id = x.program_id and m.user_id = x.user_id
            where x.program_id = @programId
              and (@userId::uuid is null or x.user_id = @userId::uuid)
              and (@from::date is null or x.on_date >= @from::date)
              and (@to::date is null or x.on_date <= @to::date)
            order by x.on_date, x.user_id
            limit 5000
            """, new { programId, userId, from, to });

    /// <summary>Like <see cref="Daily"/> but for one person whatever their membership (a person always sees their own entries).</summary>
    public Task<List<LpDailyRow>> DailyOf(Session s, Guid programId, Guid userId, string? from, string? to) =>
        s.Query<LpDailyRow>(
            """
            select x.user_id, to_char(x.on_date, 'YYYY-MM-DD') as on_date, x."values"::text as values_json, x.updated_at
            from lp_daily_status x
            where x.program_id = @programId and x.user_id = @userId
              and (@from::date is null or x.on_date >= @from::date)
              and (@to::date is null or x.on_date <= @to::date)
            order by x.on_date
            limit 5000
            """, new { programId, userId, from, to });

    public Task<LpDailyRow> SaveDaily(Session s, Guid programId, Guid userId, string onDate, string valuesJson) =>
        s.First<LpDailyRow>(
            """
            insert into lp_daily_status as x (program_id, user_id, on_date, "values") values (@programId, @userId, @onDate::date, @valuesJson::jsonb)
            on conflict (program_id, user_id, on_date) do update set "values" = excluded."values", updated_at = now()
            returning x.user_id, to_char(x.on_date, 'YYYY-MM-DD') as on_date, x."values"::text as values_json, x.updated_at
            """, new { programId, userId, onDate, valuesJson })!;

    public Task<List<LpDayStatusRow>> DayStatus(Session s, Guid programId, Guid? userId, bool membersOnly) =>
        s.Query<LpDayStatusRow>(
            """
            select x.user_id, x.day_id, x."values"::text as values_json, x.completed, x.updated_at
            from lp_day_status x join lp_days d on d.id = x.day_id
            where x.program_id = @programId
              and (@userId::uuid is null or x.user_id = @userId::uuid)
              and (not @membersOnly or exists (select 1 from lp_members m where m.program_id = x.program_id and m.user_id = x.user_id))
            order by d.week, d.day, x.user_id
            limit 10000
            """, new { programId, userId, membersOnly });

    public Task<LpDayStatusRow> SaveDayStatus(Session s, Guid programId, Guid userId, Guid dayId, string valuesJson, bool completed) =>
        s.First<LpDayStatusRow>(
            """
            insert into lp_day_status as x (program_id, user_id, day_id, "values", completed, completed_at)
            values (@programId, @userId, @dayId, @valuesJson::jsonb, @completed, case when @completed then now() end)
            on conflict (user_id, day_id) do update set
              "values" = excluded."values",
              completed = excluded.completed,
              completed_at = case when not excluded.completed then null else coalesce(x.completed_at, now()) end,
              updated_at = now()
            returning x.user_id, x.day_id, x."values"::text as values_json, x.completed, x.updated_at
            """, new { programId, userId, dayId, valuesJson, completed })!;

    /// <summary>The people a report shows: one person, or every member of the program (coordinators included), by name.</summary>
    public Task<List<LpPersonView>> People(Session s, Guid programId, Guid? onlyUserId) =>
        s.Query<LpPersonView>(
            $"""
             select u.id as user_id, {NameOf("u")} as name, u.email
             from app_users u
             where (@onlyUserId::uuid is not null and u.id = @onlyUserId::uuid)
                or (@onlyUserId::uuid is null and exists (select 1 from lp_members m where m.program_id = @programId and m.user_id = u.id))
             order by lower({NameOf("u")}), u.email
             """, new { programId, onlyUserId });
}
