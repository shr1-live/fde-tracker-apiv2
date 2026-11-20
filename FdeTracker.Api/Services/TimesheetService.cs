using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;

namespace FdeTracker.Api.Services;

public sealed class TimesheetCellRequest
{
    [Required] public string Date { get; set; } = "";
    public int Minutes { get; set; }
    public string? Note { get; set; }
}

public sealed class TimesheetTaskRequest
{
    public string Title { get; set; } = "";
    public List<TimesheetCellRequest> Cells { get; set; } = [];
}

/// <summary>The whole week at once: the tasks in order, each with its days. Submit stamps the week as handed in.</summary>
public sealed class TimesheetRequest
{
    public List<TimesheetTaskRequest> Tasks { get; set; } = [];
    public bool Submit { get; set; }
}

public sealed record TimesheetCellView(string Date, int Minutes, string? Note);
public sealed record TimesheetTaskView(Guid Id, string Title, List<TimesheetCellView> Cells);
public sealed record TimesheetView(Guid UserId, string Week, DateTime? SubmittedAt, DateTime? UpdatedAt, List<TimesheetTaskView> Tasks);

/// <summary>
/// Daily status as a timesheet. A person keeps their own week: tasks they add, hours and a note per day. A member sees only
/// their own; a coordinator or the admin may read anyone's in the program. Hours on a day count as that day's status,
/// so they also settle that day's reminder.
/// </summary>
public sealed class TimesheetService(Db db, LearningProgramRepository programs, NotificationRepository notifications)
{
    public const int MaxTasks = 30;
    public const int MaxTitle = 120;
    public const int MaxNote = 500;

    static readonly DateOnly Earliest = new(2020, 1, 6); // a Monday
    static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static DateOnly Tomorrow => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    static DateOnly Monday(string? week, string name)
    {
        if (!DateOnly.TryParseExact(week, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw ApiException.BadRequest($"{name} must be a date (YYYY-MM-DD)");
        if (d.DayOfWeek != DayOfWeek.Monday) throw ApiException.BadRequest($"{name} must be a Monday");
        if (d < Earliest || d > Tomorrow) throw ApiException.BadRequest($"{name} must be between 2020-01-06 and this week");
        return d;
    }

    public Task<TimesheetView> Get(AppUser user, Guid programId, string week, Guid? userId) => db.Read(async s =>
    {
        var a = await LearningProgramService.Access(s, programs, user, programId);
        var who = userId ?? user.Id;
        if (who != user.Id && !a.CanManage) throw new ApiException(403, "You can only see your own timesheet");
        return await Load(s, programId, who, Monday(week, "week"));
    });

    public Task<TimesheetView> Save(AppUser user, Guid programId, string week, TimesheetRequest b) => db.Tx(async s =>
    {
        await LearningProgramService.Access(s, programs, user, programId);
        var monday = Monday(week, "week");
        if (b.Tasks.Count > MaxTasks) throw ApiException.BadRequest($"At most {MaxTasks} tasks a week");
        var perDay = new Dictionary<DateOnly, int>();
        var clean = new List<(string Title, List<(DateOnly Date, int Minutes, string? Note)> Cells)>();
        foreach (var t in b.Tasks)
        {
            var title = (t.Title ?? "").Trim();
            if (title.Length is 0 or > MaxTitle) throw ApiException.BadRequest($"Every task needs a name of 1 to {MaxTitle} characters");
            var cells = new List<(DateOnly, int, string?)>();
            foreach (var c in t.Cells ?? [])
            {
                if (!DateOnly.TryParseExact(c.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d < monday || d > monday.AddDays(6))
                    throw ApiException.BadRequest("Every day must be inside the week");
                if (d > Tomorrow) throw ApiException.BadRequest("You cannot log time for days still to come");
                if (c.Minutes is < 0 or > 1440) throw ApiException.BadRequest("Time per day must be from 0:00 to 24:00");
                var note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim();
                if (note?.Length > MaxNote) throw ApiException.BadRequest($"A note can be at most {MaxNote} characters");
                if (c.Minutes == 0 && note is null) continue; // an empty cell is not stored
                if (cells.Any(x => x.Item1 == d)) throw ApiException.BadRequest("A task can have one entry per day");
                perDay[d] = perDay.GetValueOrDefault(d) + c.Minutes;
                if (perDay[d] > 1440) throw ApiException.BadRequest($"More than 24 hours logged on {Iso(d)}");
                cells.Add((d, c.Minutes, note));
            }
            clean.Add((title, cells));
        }

        // Replace the week in one go: simplest to reason about, and the week is small.
        await s.Exec("delete from lp_time_tasks where program_id = @programId and user_id = @userId and week_start = @week::date",
            new { programId, userId = user.Id, week = Iso(monday) });
        var pos = 0;
        foreach (var (title, cells) in clean)
        {
            var id = await s.Scalar<Guid>(
                "insert into lp_time_tasks (program_id, user_id, week_start, title, position) values (@programId, @userId, @week::date, @title, @pos) returning id",
                new { programId, userId = user.Id, week = Iso(monday), title, pos = pos++ });
            foreach (var (d, minutes, note) in cells)
                await s.Exec("insert into lp_time_cells (task_id, on_date, minutes, note) values (@id, @date::date, @minutes, @note)", new { id, date = Iso(d), minutes, note });
        }
        await s.Exec("""
            insert into lp_time_weeks (program_id, user_id, week_start, submitted_at, updated_at) values (@programId, @userId, @week::date, case when @submit then now() end, now())
            on conflict (program_id, user_id, week_start) do update set submitted_at = case when @submit then now() end, updated_at = now()
            """, new { programId, userId = user.Id, week = Iso(monday), submit = b.Submit });
        foreach (var d in perDay.Where(x => x.Value > 0).Select(x => x.Key))
            await notifications.SettleDailyMissed(s, user.Id, programId, Iso(d)); // hours on a day count as that day's status
        return await Load(s, programId, user.Id, monday);
    });

    sealed class TaskRow { public Guid Id { get; set; } public string Title { get; set; } = ""; }
    sealed class CellRow { public Guid TaskId { get; set; } public string OnDate { get; set; } = ""; public int Minutes { get; set; } public string? Note { get; set; } }
    sealed class WeekRow { public DateTime? SubmittedAt { get; set; } public DateTime UpdatedAt { get; set; } }

    static async Task<TimesheetView> Load(Session s, Guid programId, Guid userId, DateOnly monday)
    {
        var args = new { programId, userId, week = Iso(monday) };
        var tasks = await s.Query<TaskRow>("select id, title from lp_time_tasks where program_id = @programId and user_id = @userId and week_start = @week::date order by position, created_at", args);
        var cells = await s.Query<CellRow>("""
            select c.task_id, to_char(c.on_date, 'YYYY-MM-DD') as on_date, c.minutes, c.note
            from lp_time_cells c join lp_time_tasks t on t.id = c.task_id
            where t.program_id = @programId and t.user_id = @userId and t.week_start = @week::date order by c.on_date
            """, args);
        var w = await s.First<WeekRow>("select submitted_at, updated_at from lp_time_weeks where program_id = @programId and user_id = @userId and week_start = @week::date", args);
        var byTask = cells.GroupBy(c => c.TaskId).ToDictionary(g => g.Key, g => g.Select(c => new TimesheetCellView(c.OnDate, c.Minutes, c.Note)).ToList());
        return new TimesheetView(userId, Iso(monday), w?.SubmittedAt, w?.UpdatedAt, tasks.Select(t => new TimesheetTaskView(t.Id, t.Title, byTask.GetValueOrDefault(t.Id) ?? [])).ToList());
    }
}
