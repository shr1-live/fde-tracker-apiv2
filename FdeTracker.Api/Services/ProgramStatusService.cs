using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Programs V3: daily status, day status and reports. A member sees only themselves; a coordinator or the admin sees everyone in the program.</summary>
public sealed class ProgramStatusService(Db db, LearningProgramRepository repo)
{
    public const int MaxReportDays = 120;
    static readonly DateOnly EarliestDaily = new(2020, 1, 1);

    static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly? ParseDate(string? v, string name)
    {
        if (string.IsNullOrEmpty(v)) return null;
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw ApiException.BadRequest($"{name} must be a date (YYYY-MM-DD)");
    }

    /// <summary>Whose entries the caller may read: themselves always; anyone else only as coordinator or admin (403 otherwise).
    /// Null means "everyone in the program".</summary>
    static Guid? Whose(ProgramAccess a, AppUser user, Guid? asked)
    {
        if (asked is { } id && id != user.Id && !a.CanManage) throw new ApiException(403, "You can only see your own status");
        return asked ?? (a.CanManage ? null : user.Id);
    }

    // ---- daily status

    public Task<List<LpDailyView>> Daily(AppUser user, Guid programId, Guid? userId, string? from, string? to) => db.Read(async s =>
    {
        var a = await LearningProgramService.Access(s, repo, user, programId);
        var f = ParseDate(from, "from"); var t = ParseDate(to, "to");
        var who = Whose(a, user, userId);
        var rows = who == user.Id
            ? await repo.DailyOf(s, programId, user.Id, f is { } fd ? Iso(fd) : null, t is { } td ? Iso(td) : null)
            : await repo.Daily(s, programId, who, f is { } fd2 ? Iso(fd2) : null, t is { } td2 ? Iso(td2) : null);
        return rows.Select(LpDailyView.From).ToList();
    });

    public Task<LpDailyView> SaveDaily(AppUser user, Guid programId, string date, JsonElement values) => db.Tx(async s =>
    {
        await LearningProgramService.Access(s, repo, user, programId);
        var d = ParseDate(date, "date")!.Value;
        if (d < EarliestDaily || d > Today.AddDays(1)) throw ApiException.BadRequest("The date must be between 2020-01-01 and tomorrow");
        var fields = (await repo.Fields(s, programId)).Where(f => f.Form == "daily").ToList();
        var clean = FieldValues.Check(fields, values, enforceRequired: true);
        return LpDailyView.From(await repo.SaveDaily(s, programId, user.Id, Iso(d), clean));
    });

    // ---- day status

    public Task<List<LpDayStatusView>> DayStatus(AppUser user, Guid programId, Guid? userId) => db.Read(async s =>
    {
        var a = await LearningProgramService.Access(s, repo, user, programId);
        var who = Whose(a, user, userId);
        var rows = await repo.DayStatus(s, programId, who, membersOnly: who is null);
        return rows.Select(LpDayStatusView.From).ToList();
    });

    public Task<LpDayStatusView> SaveDayStatus(AppUser user, Guid programId, Guid dayId, JsonElement values, bool completed) => db.Tx(async s =>
    {
        await LearningProgramService.Access(s, repo, user, programId);
        if (await repo.Day(s, programId, dayId) is null) throw ApiException.NotFound();
        var fields = (await repo.Fields(s, programId)).Where(f => f.Form == "day").ToList();
        var clean = FieldValues.Check(fields, values, enforceRequired: completed);
        return LpDayStatusView.From(await repo.SaveDayStatus(s, programId, user.Id, dayId, clean, completed));
    });

    // ---- reports

    public Task<LpDailyReportView> DailyReport(AppUser user, Guid programId, string? from, string? to) => db.Read(async s =>
    {
        var a = await LearningProgramService.Access(s, repo, user, programId);
        var t = ParseDate(to, "to") ?? Today;
        var f = ParseDate(from, "from") ?? DefaultFrom(a.StartsOn, t);
        if (f > t) throw ApiException.BadRequest("from must not be after to");
        if (t.DayNumber - f.DayNumber + 1 > MaxReportDays) throw ApiException.BadRequest($"A report covers at most {MaxReportDays} days");

        var people = await repo.People(s, programId, a.CanManage ? null : user.Id);
        var rows = a.CanManage
            ? await repo.Daily(s, programId, null, Iso(f), Iso(t))
            : await repo.DailyOf(s, programId, user.Id, Iso(f), Iso(t));
        var fields = (await repo.Fields(s, programId)).Where(x => x.Form == "daily").ToList();

        var withEntries = rows.Select(r => r.OnDate).ToHashSet();
        var dates = new List<string>();
        for (var d = f; d <= t; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || withEntries.Contains(Iso(d))) dates.Add(Iso(d));

        return new LpDailyReportView
        {
            Fields = fields, People = people, Dates = dates,
            Cells = rows.Select(r => new LpDailyCellView { UserId = r.UserId, OnDate = r.OnDate, Values = LpJson.Parse(r.ValuesJson) }).ToList(),
            From = f, To = t, ProgramName = a.Program.Name, StartsOn = a.StartsOn,
        };
    });

    /// <summary>The program start (or 30 days ago), but never more than the longest range before the end, and never after it.</summary>
    static DateOnly DefaultFrom(DateOnly? start, DateOnly to)
    {
        var f = start ?? to.AddDays(-29);
        var earliest = to.AddDays(-(MaxReportDays - 1));
        if (f < earliest) f = earliest;
        return f > to ? to : f;
    }

    public Task<LpProgramReportView> ProgramReport(AppUser user, Guid programId) => db.Read(async s =>
    {
        var a = await LearningProgramService.Access(s, repo, user, programId);
        var days = await repo.Days(s, programId);
        var fields = (await repo.Fields(s, programId)).Where(x => x.Form == "day").ToList();
        var people = await repo.People(s, programId, a.CanManage ? null : user.Id);
        var status = await repo.DayStatus(s, programId, a.CanManage ? null : user.Id, membersOnly: false);
        var byKey = status.ToDictionary(x => (x.UserId, x.DayId));
        return new LpProgramReportView
        {
            Days = days, Fields = fields, ProgramName = a.Program.Name,
            People = people.Select(p => new LpReportPersonView
            {
                UserId = p.UserId, Name = p.Name, Email = p.Email,
                Rows = days.Select(d => byKey.TryGetValue((p.UserId, d.Id), out var r)
                    ? new LpReportRowView { DayId = d.Id, Values = LpJson.Parse(r.ValuesJson), Completed = r.Completed }
                    : new LpReportRowView { DayId = d.Id, Values = LpJson.Empty, Completed = false }).ToList(),
            }).ToList(),
        };
    });
}

/// <summary>Checks a status "values" object against a form and returns the cleaned JSON to store (empty values dropped, text trimmed).</summary>
public static class FieldValues
{
    public static string Check(IReadOnlyList<LpFieldView> fields, JsonElement values, bool enforceRequired)
    {
        if (values.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) values = LpJson.Empty;
        if (values.ValueKind != JsonValueKind.Object) throw ApiException.BadRequest("values must be an object");
        var byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var clean = new JsonObject();
        foreach (var p in values.EnumerateObject())
        {
            if (!byKey.TryGetValue(p.Name, out var f)) throw ApiException.BadRequest($"Unknown field: {Short(p.Name)}");
            if (clean.ContainsKey(p.Name)) throw ApiException.BadRequest($"{f.Label}: given twice");
            var v = Value(f, p.Value);
            if (v is not null) clean[p.Name] = v;
        }
        if (enforceRequired)
            foreach (var f in fields.Where(f => f.Required && !clean.ContainsKey(f.Key)))
                throw ApiException.BadRequest($"{f.Label} is required");
        return clean.ToJsonString();
    }

    static string Short(string s) => s.Length > 40 ? s[..40] : s;

    /// <summary>The value to store, or null when it is empty (counts as missing).</summary>
    static JsonNode? Value(LpFieldView f, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString())) return null;
        switch (f.Type)
        {
            case "text":
            case "longtext":
            {
                if (v.ValueKind != JsonValueKind.String) throw ApiException.BadRequest($"{f.Label} must be text");
                var text = v.GetString()!.Trim();
                var max = f.Type == "text" ? 500 : 4000;
                if (text.Length > max) throw ApiException.BadRequest($"{f.Label}: at most {max} characters");
                return JsonValue.Create(text);
            }
            case "url":
            {
                if (v.ValueKind != JsonValueKind.String) throw ApiException.BadRequest($"{f.Label} must be a link");
                var url = v.GetString()!.Trim();
                if (url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || url.Any(char.IsWhiteSpace))
                    throw ApiException.BadRequest($"{f.Label} must be a link starting with https:// (at most 500 characters)");
                return JsonValue.Create(url);
            }
            case "number":
            {
                var n = Number(f, v);
                if (n < 0 || n > 1000) throw ApiException.BadRequest($"{f.Label} must be a number from 0 to 1000");
                return n == Math.Floor(n) ? JsonValue.Create((long)n) : JsonValue.Create(n);
            }
            case "rating":
            {
                var n = Number(f, v);
                if (n != Math.Floor(n) || n < 1 || n > 5) throw ApiException.BadRequest($"{f.Label} must be a whole number from 1 to 5");
                return JsonValue.Create((int)n);
            }
            default: throw ApiException.BadRequest($"{f.Label}: unknown field type");
        }
    }

    static double Number(LpFieldView f, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString()!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && double.IsFinite(p)) return p;
        throw ApiException.BadRequest($"{f.Label} must be a number");
    }
}
