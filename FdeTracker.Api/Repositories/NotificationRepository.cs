using FdeTracker.Api.Data;

namespace FdeTracker.Api.Repositories;

public sealed class NotificationRow
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public Guid? ProgramId { get; set; }
    public string? OnDate { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>A reminder that was just created and still needs its email.</summary>
public sealed class NewReminderRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string OnDate { get; set; } = "";
    public string? Link { get; set; }
}

/// <summary>The notifications table. Parameterised SQL only.</summary>
public sealed class NotificationRepository
{
    const string Cols = "n.id, n.kind, n.program_id, to_char(n.on_date, 'YYYY-MM-DD') as on_date, n.title, n.body, n.link, n.created_at, n.read_at";

    public Task<List<NotificationRow>> ForUser(Session s, Guid userId, int limit) =>
        s.Query<NotificationRow>($"select {Cols} from notifications n where n.user_id = @userId order by n.created_at desc, n.id desc limit @limit", new { userId, limit });

    public Task<int> UnreadCount(Session s, Guid userId) =>
        s.Scalar<int>("select count(*)::int from notifications where user_id = @userId and read_at is null", new { userId });

    public Task<int> MarkRead(Session s, Guid userId, Guid id) =>
        s.Exec("update notifications set read_at = now() where id = @id and user_id = @userId and read_at is null", new { id, userId });

    public Task<int> MarkAllRead(Session s, Guid userId) =>
        s.Exec("update notifications set read_at = now() where user_id = @userId and read_at is null", new { userId });

    /// <summary>Filling in the missed day settles its reminder.</summary>
    public Task<int> SettleDailyMissed(Session s, Guid userId, Guid programId, string onDate) =>
        s.Exec("""
            update notifications set read_at = now()
            where user_id = @userId and kind = 'daily_missed' and program_id = @programId and on_date = @onDate::date and read_at is null
            """, new { userId, programId, onDate });

    /// <summary>
    /// Creates one 'daily_missed' reminder per member (or coordinator) and program with no daily status and no timesheet hours on <paramref name="onDate"/>.
    /// Only programs that had started by then, and only people who were already in the program that day. Safe to run again:
    /// the unique index skips reminders that exist. Pass <paramref name="onlyUserId"/> to do it for one person.
    /// </summary>
    public Task<List<NewReminderRow>> CreateDailyMissed(Session s, string onDate, Guid? onlyUserId) =>
        s.Query<NewReminderRow>($"""
            with missed as (
              select m.user_id, p.id as program_id, p.name as program_name
              from lp_members m
              join lp_programs p on p.id = m.program_id
              join app_users u on u.id = m.user_id
              where (p.starts_on is null or p.starts_on <= @onDate::date)
                and m.added_at::date <= @onDate::date
                and u.access_status = 'approved'
                and (@onlyUserId::uuid is null or m.user_id = @onlyUserId::uuid)
                and not exists (select 1 from lp_daily_status x where x.program_id = m.program_id and x.user_id = m.user_id and x.on_date = @onDate::date)
                and not exists (select 1 from lp_time_cells c join lp_time_tasks t on t.id = c.task_id
                                where t.program_id = m.program_id and t.user_id = m.user_id and c.on_date = @onDate::date and c.minutes > 0)
            ), added as (
              insert into notifications (user_id, kind, program_id, on_date, title, body, link)
              select user_id, 'daily_missed', program_id, @onDate::date,
                     left('Fill in your daily status for ' || program_name, 200),
                     'You have not filled in your ' || program_name || ' daily status for ' || to_char(@onDate::date, 'Dy DD Mon') || '.',
                     '/p/' || program_id || '/daily?date=' || @onDate
              from missed
              on conflict (user_id, kind, program_id, on_date) do nothing
              returning id, user_id, program_id, link
            )
            select a.id, a.user_id, u.email, {LearningProgramRepository.NameOf("u")} as name, p.name as program_name, @onDate as on_date, a.link
            from added a join app_users u on u.id = a.user_id join lp_programs p on p.id = a.program_id
            """, new { onDate, onlyUserId });

    public Task<int> MarkEmailed(Session s, Guid id) => s.Exec("update notifications set emailed_at = now() where id = @id", new { id });
}
