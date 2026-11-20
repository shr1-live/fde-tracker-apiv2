using System.Globalization;
using System.Net.Http.Json;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;

namespace FdeTracker.Api.Services;

public sealed record NotificationView(Guid Id, string Kind, Guid? ProgramId, string? OnDate, string Title, string Body, string? Link, DateTime CreatedAt, bool Read);
public sealed record NotificationList(List<NotificationView> Items, int Unread);

/// <summary>
/// In-app notifications and the daily status reminder. At <see cref="AppOptions.ReminderHour"/> (company time zone) every member
/// who missed the previous working day's daily status in a program gets one reminder for that program, in the app and by email.
/// Example: on Thu 19 Nov someone fills in DevOps but not FDE; at 10:00 on Fri 20 Nov they get "Fill in your daily status for FDE".
/// Monday reminds about Friday. Reminders are unique per person, program and day, so every run (the timer, a restart,
/// the catch-up when someone opens the app while the free server was asleep) is safe to repeat.
/// </summary>
public sealed class NotificationService(Db db, NotificationRepository repo, AppOptions options, IHttpClientFactory http, ILogger<NotificationService> log)
{
    TimeZoneInfo Zone => ResolveZone(options.ReminderTimeZone);

    /// <summary>
    /// The company time zone. The API runs with invariant globalization (and the container may have no time zone data), where
    /// "Asia/Kolkata" cannot be looked up; India has no daylight saving, so a fixed UTC+05:30 is exact for it.
    /// </summary>
    public static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win)) { try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { } }
        return id is "Asia/Kolkata" or "Asia/Calcutta"
            ? TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "India Standard Time")
            : throw new InvalidOperationException($"REMINDER_TIME_ZONE '{id}' is not known on this server");
    }

    /// <summary>The working day a reminder at <paramref name="nowUtc"/> is about, or null before the reminder hour or at weekends.</summary>
    public static DateOnly? DayToRemind(DateTime nowUtc, TimeZoneInfo zone, int hour)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local.Hour < hour) return null;
        var d = DateOnly.FromDateTime(local).AddDays(-1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }

    /// <summary>Creates the due reminders (for everyone, or one person) and emails the new ones. Returns how many were created.</summary>
    public async Task<int> RunReminders(Guid? onlyUserId = null, DateTime? nowUtc = null)
    {
        if (nowUtc is null && !options.RemindersEnabled) return 0; // a fixed clock (tests) runs even when the timer is off
        if (DayToRemind(nowUtc ?? DateTime.UtcNow, Zone, options.ReminderHour) is not { } day) return 0;
        var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var created = await db.Tx(s => repo.CreateDailyMissed(s, iso, onlyUserId));
        foreach (var r in created) await Email(r);
        if (created.Count > 0) log.LogInformation("Daily status reminders for {Day}: {Count}", iso, created.Count);
        return created.Count;
    }

    async Task Email(NewReminderRow r)
    {
        if (options.ResendApiKey is not { Length: > 0 } key || options.EmailFrom is not { Length: > 0 } from) return; // email is optional
        try
        {
            var link = options.AppUrl.TrimEnd('/') + (r.Link ?? "/");
            var date = DateOnly.ParseExact(r.OnDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dddd d MMM", CultureInfo.InvariantCulture);
            var text = $"Hi {r.Name},\n\nYou have not filled in your {r.ProgramName} daily status for {date}.\nFill it in here: {link}\n\nFDE Tracker";
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
            {
                Content = JsonContent.Create(new { from, to = new[] { r.Email }, subject = $"Reminder: {r.ProgramName} daily status for {date}", text }),
            };
            req.Headers.Authorization = new("Bearer", key);
            using var res = await http.CreateClient("email").SendAsync(req);
            if (res.IsSuccessStatusCode) await db.Read(s => repo.MarkEmailed(s, r.Id));
            else log.LogWarning("Reminder email failed with {Status}", (int)res.StatusCode);
        }
        catch (Exception e) { log.LogWarning(e, "Reminder email failed"); } // the in-app reminder is already saved
    }

    public async Task<NotificationList> List(AppUser user)
    {
        // Catch up if the free server slept through 10:00. A failure here must never hide the notifications already saved.
        try { await RunReminders(user.Id); } catch (Exception e) { log.LogWarning(e, "Reminder catch-up failed"); }
        return await db.Read(async s =>
        {
            var rows = await repo.ForUser(s, user.Id, 30);
            var unread = await repo.UnreadCount(s, user.Id);
            return new NotificationList(rows.Select(n => new NotificationView(n.Id, n.Kind, n.ProgramId, n.OnDate, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt is not null)).ToList(), unread);
        });
    }

    public Task MarkRead(AppUser user, Guid id) => db.Read(s => repo.MarkRead(s, user.Id, id));
    public Task MarkAllRead(AppUser user) => db.Read(s => repo.MarkAllRead(s, user.Id));
}

/// <summary>Checks every few minutes whether reminders are due. Cheap when nothing is: the unique index makes repeats no-ops.</summary>
public sealed class ReminderJob(NotificationService service, ILogger<ReminderJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await service.RunReminders(); }
            catch (Exception e) { log.LogWarning(e, "Reminder run failed; trying again in 5 minutes"); }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
