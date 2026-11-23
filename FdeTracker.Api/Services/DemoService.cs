using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FdeTracker.Api.Common;
using FdeTracker.Api.Data;

namespace FdeTracker.Api.Services;

public sealed record DemoStatus(bool Loaded, int Programs, int People);

/// <summary>What the two training sheets hold: the plan, and per person their day rows and their "Day N" daily status.</summary>
public sealed record SheetDay(string Learned, string Proof, string Rate);
public sealed record SheetPlanDay(int Week, string Topic, string Cover, string Task);
public sealed record SheetPerson(string Name, List<SheetDay> Days, Dictionary<int, string> Daily);
public sealed record SheetData(List<SheetPlanDay> Plan, List<SheetPerson> People);

/// <summary>
/// Demo data for showing the product on the live site. The admin loads it from the Programs page and removes it the same way.
/// Everything is kept apart from real data: demo programs carry <c>is_demo</c>, demo people have <c>@demo.local</c> emails
/// (they can never sign in, and reminders never email them). Removing deletes exactly those rows; real data is never touched.
/// The demo FDE program copies the real FDE program's forms and plan, so it looks like the real thing.
/// </summary>
public sealed class DemoService(Db db)
{
    public const string Domain = "demo.local";
    public static bool IsDemoEmail(string email) => email.EndsWith("@" + Domain, StringComparison.OrdinalIgnoreCase);

    sealed record Who(string Key, string Name);
    // The keys are only internal handles (and the demo email); the names are what people see. The feed is posted by the Shivanshu accounts,
    // with a few other names in between.
    static readonly Who[] People =
    [
        new("priya", "Shivanshu Admin"), new("rahul", "Shivanshu Coordinator"), new("asha", "Shivanshu 1"), new("karan", "Shivanshu 2"),
        new("neha", "Shivanshu 3"), new("arjun", "Ananya Iyer"), new("meera", "Divya Menon"), new("vikram", "Rohan Desai"),
    ];

    static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static bool Weekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public Task<DemoStatus> Status() => db.Read(async s => new DemoStatus(
        await s.Scalar<int>("select count(*)::int from lp_programs where is_demo") > 0,
        await s.Scalar<int>("select count(*)::int from lp_programs where is_demo"),
        await s.Scalar<int>("select count(*)::int from app_users where email like '%@' || @d", new { d = Domain })));

    public Task<DemoStatus> Remove() => db.Tx(async s =>
    {
        await s.Exec("delete from lp_programs where is_demo");                        // cascades: members, plan, forms, status, timesheets, feed, reminders
        await s.Exec("delete from app_users where email like '%@' || @d", new { d = Domain }); // their likes and comments elsewhere go too
        return new DemoStatus(false, 0, 0);
    });

    sealed class FieldRow { public string Form { get; set; } = ""; public string Key { get; set; } = ""; public string Type { get; set; } = ""; }
    sealed class DayRow { public Guid Id { get; set; } public string Topic { get; set; } = ""; }

    /// <summary>
    /// Reads the program status sheet (one tab per person: Week, Topic, Topics to Cover, Practical Task, What You Learn,
    /// Proof of Evidence, Rate) and the 30-day daily status sheet (tab "Status": Name, Day 1..Day 30). Either may be missing.
    /// </summary>
    public static SheetData ReadSheets(Stream? report, Stream? daily)
    {
        static string T(IXLCell c) => c.GetFormattedString().Replace("\u2022", "-").Trim();
        var plan = new List<SheetPlanDay>();
        var people = new Dictionary<string, SheetPerson>(StringComparer.OrdinalIgnoreCase);
        SheetPerson Person(string name) => people.TryGetValue(name, out var p) ? p : people[name] = new SheetPerson(name, [], []);
        if (report is not null)
        {
            using var wb = new XLWorkbook(report);
            foreach (var ws in wb.Worksheets)
            {
                if (!string.Equals(T(ws.Cell(1, 1)), "Week", StringComparison.OrdinalIgnoreCase)) continue; // the guide tab
                var week = 0; var rows = new List<(SheetPlanDay Plan, SheetDay Day)>();
                for (var r = 2; r <= Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 200); r++)
                {
                    var w = T(ws.Cell(r, 1));
                    if (w.StartsWith("Week", StringComparison.OrdinalIgnoreCase) && int.TryParse(Regex.Replace(w, @"\D", ""), out var wn)) week = wn;
                    var topic = T(ws.Cell(r, 2));
                    if (topic.Length == 0) continue;
                    rows.Add((new SheetPlanDay(Math.Max(week, 1), topic, T(ws.Cell(r, 3)), T(ws.Cell(r, 4))), new SheetDay(T(ws.Cell(r, 5)), T(ws.Cell(r, 6)), T(ws.Cell(r, 7)))));
                }
                if (plan.Count == 0) plan.AddRange(rows.Select(x => x.Plan));
                Person(ws.Name.Trim()).Days.AddRange(rows.Select(x => x.Day));
            }
        }
        if (daily is not null)
        {
            using var wb = new XLWorkbook(daily);
            if (wb.Worksheets.TryGetWorksheet("Status", out var ws))
            {
                var cols = new Dictionary<int, int>();
                for (var c = 2; c <= (ws.LastColumnUsed()?.ColumnNumber() ?? 1); c++)
                    if (Regex.Match(T(ws.Cell(1, c)), @"^Day (\d+)$") is { Success: true } m) cols[c] = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                for (var r = 2; r <= Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 500); r++)
                {
                    var name = T(ws.Cell(r, 1));
                    if (name.Length == 0) continue;
                    foreach (var (c, n) in cols) if (T(ws.Cell(r, c)) is { Length: > 0 } v) Person(name).Daily[n] = v;
                }
            }
        }
        return new SheetData(plan, people.Values.Where(p => p.Days.Count > 0 || p.Daily.Count > 0).Take(60).ToList());
    }

    /// <summary>"Project Worked: Vector7 - 2 hours / Training Theory: ... / Practical: ..." split into the three parts.</summary>
    static (string Project, string Theory, string Practical) ParseDaily(string text)
    {
        var parts = new Dictionary<string, string> { ["project"] = "", ["theory"] = "", ["practical"] = "" };
        var cur = "project";
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(line, @"^\s*(project(?:\s*worked)?|training\s*theory|theory|practical)\s*:?\s*(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var k = m.Groups[1].Value.ToLowerInvariant();
                cur = k.Contains("project") ? "project" : k.Contains("theory") ? "theory" : "practical";
                parts[cur] = m.Groups[2].Value.Trim();
            }
            else parts[cur] = (parts[cur] + " " + line.Trim()).Trim();
        }
        return (parts["project"], parts["theory"], parts["practical"]);
    }
    static double HoursIn(string s) => Regex.Match(s, @"(\d+(?:\.\d+)?)\s*(?:hours?|hrs?|h)\b", RegexOptions.IgnoreCase) is { Success: true } m ? Math.Min(10, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : 0;
    static int Minutes(double h, double fallback) => (int)Math.Round((h > 0 ? h : fallback) * 60);
    static string Cut(string s, int n) => s.Length <= n ? s : s[..n];

    public Task<DemoStatus> Load(DateOnly? todayOverride = null, SheetData? sheet = null) => db.Tx(async s =>
    {
        if (await s.Scalar<int>("select count(*)::int from lp_programs where is_demo") > 0) throw new ApiException(409, "Demo data is already loaded. Remove it first to load it again.");
        var today = todayOverride ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var start = monday.AddDays(-7);        // FDE demo: week 1 was last week (complete sample data), week 2 is this week
        var devStart = monday.AddDays(-7);     // DevOps demo: started last week too
        var yesterday = today.AddDays(-1);
        while (Weekend(yesterday)) yesterday = yesterday.AddDays(-1);

        // ---- people
        var ids = new Dictionary<string, Guid>();
        foreach (var p in People)
        {
            ids[p.Key] = Guid.NewGuid();
            await s.Exec("insert into app_users (id, email, display_name, role, access_status) values (@id, @email, @name, 'member', 'approved')",
                new { id = ids[p.Key], email = $"{p.Key}@{Domain}", name = p.Name });
        }

        // ---- programs: forms (and the FDE plan) copied from the real FDE program when it exists
        var real = await s.First<Guid?>("select id from lp_programs where name = 'FDE Training Program' and not is_demo order by created_at limit 1");
        async Task<Guid> Program(string name, string desc, DateOnly starts) =>
            await s.Scalar<Guid>("insert into lp_programs (name, description, starts_on, is_demo) values (@name, @desc, @starts::date, true) returning id", new { name, desc, starts = Iso(starts) });
        var fde = await Program("FDE Training Program (Demo)", "Sample data for demos: Forward Deployed Engineer training, from LLM fundamentals to a delivered assistant.", start);
        var dev = await Program("DevOps Training Program (Demo)", "Sample data for demos: CI/CD, containers, Kubernetes and cloud basics.", devStart);
        foreach (var pid in new[] { fde, dev })
        {
            if (real is { } r)
                await s.Exec("""
                    insert into lp_form_fields (program_id, form, key, label, description, type, required, position)
                    select @pid, form, key, label, description, type, required, position from lp_form_fields where program_id = @r
                    """, new { pid, r });
            else
                await s.Exec("""
                    insert into lp_form_fields (program_id, form, key, label, description, type, required, position) values
                    (@pid, 'daily', 'project', 'Project worked', null, 'text', true, 0), (@pid, 'daily', 'theory', 'Training theory', null, 'longtext', true, 1),
                    (@pid, 'daily', 'practical', 'Practical', null, 'longtext', true, 2), (@pid, 'day', 'learned', 'What You Learn', null, 'longtext', true, 0),
                    (@pid, 'day', 'proof', 'Proof of Evidence', null, 'longtext', false, 1), (@pid, 'day', 'rating', 'Rate (1-5)', null, 'rating', true, 2)
                    """, new { pid });
        }
        var copied = real is { } rp ? await s.Exec("""
            insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
            select @fde, week, day, topic, topics_to_cover, practical_task, resources from lp_days where program_id = @rp
            """, new { fde, rp }) : 0;
        if (copied < FdePlan.Length) { await s.Exec("delete from lp_days where program_id = @fde", new { fde }); await Days(s, fde, FdePlan); } // weeks 1 and 2 must both be there
        // The DevOps demo copies the real DevOps program's 30-day plan when it exists, like the FDE demo does.
        var realDev = await s.First<Guid?>("select id from lp_programs where lower(name) = 'devops' and not is_demo order by created_at limit 1");
        var devCopied = realDev is { } rd ? await s.Exec("""
            insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task, resources)
            select @dev, week, day, topic, topics_to_cover, practical_task, resources from lp_days where program_id = @rd
            """, new { dev, rd }) : 0;
        if (devCopied < 30) { await s.Exec("delete from lp_days where program_id = @dev", new { dev }); await Days(s, dev, DevPlan); }

        // ---- who is where: a coordinator per program, one person in both, a coordinator who is a member elsewhere
        async Task Member(Guid pid, string k, string role, DateOnly since) =>
            await s.Exec("insert into lp_members (program_id, user_id, role, added_at) values (@pid, @u, @role, @since::date)", new { pid, u = ids[k], role, since = Iso(since) });
        await Member(fde, "priya", "coordinator", start);
        foreach (var k in new[] { "asha", "karan", "neha", "rahul", "vikram" }) await Member(fde, k, "member", start);
        await Member(fde, "meera", "member", monday.AddDays(2)); // joined this Wednesday
        await Member(dev, "rahul", "coordinator", devStart);
        foreach (var k in new[] { "karan", "arjun" }) await Member(dev, k, "member", devStart);
        await s.Exec("insert into lp_invites (program_id, email, role, invited_by) values (@dev, @email, 'member', @by)", new { dev, email = $"new.joiner@{Domain}", by = ids["rahul"] });

        var fields = await s.Query<FieldRow>("select form, key, type from lp_form_fields where program_id = @fde", new { fde });
        string Values(string form, int i, bool full = true, bool fde = true) => JsonSerializer.Serialize(fields.Where(f => f.Form == form).ToDictionary(f => f.Key, f => (object)(f.Type switch
        {
            "rating" => fde && form == "day" && i < Week1Sample.Days.Length ? Week1Sample.Days[i].Rate ?? 3 : 3 + i % 3,
            "number" => 2 + i % 3,
            "url" => "https://github.com/fiftyfive-demo/fde-practicals",
            _ => fde && form == "day" && i < Week1Sample.Days.Length
                ? (f.Key.Contains("proof") ? Week1Sample.Days[i].Proof : Week1Sample.Days[i].Learned)
                : form == "day"
                ? (f.Key.Contains("proof") ? $"https://github.com/fiftyfive-demo/{(fde ? "fde-practicals" : "devops-labs")}/tree/main/day-{i + 1}" : full ? (fde ? DayNotes[i % DayNotes.Length] : DevNotes[i % DevNotes.Length]) : "Halfway through the lab.")
                : f.Key.Contains("project") ? Projects[i % Projects.Length] : f.Key.Contains("theory") ? $"Day {i + 1} topics covered, about 3 h" : "Practical task in progress; pushed the first version.",
        })));

        // ---- plan progress (day status)
        async Task Progress(Guid pid, string k, int done, int open, bool fde = true)
        {
            var days = await s.Query<DayRow>("select id, topic from lp_days where program_id = @pid order by week, day", new { pid });
            for (var i = 0; i < done + open && i < days.Count; i++)
                await s.Exec("""
                    insert into lp_day_status (program_id, user_id, day_id, "values", completed, completed_at)
                    values (@pid, @u, @d, @v::jsonb, @c, case when @c then now() end)
                    """, new { pid, u = ids[k], d = days[i].Id, v = Values("day", i, i < done, fde), c = i < done });
        }
        await Progress(fde, "asha", 4, 1); await Progress(fde, "karan", 4, 1); await Progress(fde, "neha", 3, 2);   // week 1: days 1-4 done, day 5 (the end-to-end app) open
        await Progress(fde, "rahul", 4, 1); await Progress(fde, "vikram", 3, 1); await Progress(fde, "arjun", 2, 0);
        await Progress(dev, "arjun", 3, 0, false); await Progress(dev, "karan", 2, 1, false);

        // ---- daily log (timesheet) + the old daily form for the first week
        async Task Log(Guid pid, string k, DateOnly from, Func<DateOnly, bool> skip, (string Title, double[] Hours)[] tasks, bool submitPast)
        {
            for (var week = from.AddDays(-(((int)from.DayOfWeek + 6) % 7)); week <= monday; week = week.AddDays(7))
            {
                var any = false;
                for (var t = 0; t < tasks.Length; t++)
                {
                    var tid = await s.Scalar<Guid>("insert into lp_time_tasks (program_id, user_id, week_start, title, position) values (@pid, @u, @w::date, @title, @t) returning id",
                        new { pid, u = ids[k], w = Iso(week), title = tasks[t].Title, t });
                    for (var d = 0; d < 5; d++)
                    {
                        var date = week.AddDays(d);
                        if (date < from || date > today || skip(date)) continue;
                        var h = tasks[t].Hours[d % tasks[t].Hours.Length];
                        if (h <= 0) continue;
                        any = true;
                        await s.Exec("insert into lp_time_cells (task_id, on_date, minutes, note) values (@tid, @date::date, @m, @note)",
                            new { tid, date = Iso(date), m = (int)Math.Round(h * 60), note = t == 0 ? Notes[(d + t) % Notes.Length] : null });
                    }
                }
                if (any) await s.Exec("insert into lp_time_weeks (program_id, user_id, week_start, submitted_at) values (@pid, @u, @w::date, case when @sub then now() end)",
                    new { pid, u = ids[k], w = Iso(week), sub = submitPast && week < monday });
            }
        }
        var fdeTasks = new[] { ("Training theory", new[] { 3, 2.5, 3, 2, 3 }), ("Practical task", new[] { 2.5, 3, 2, 3, 2.5 }), ("Client project: Acme support bot", new[] { 2, 2, 2.5, 2, 1.5 }) };
        await Log(fde, "asha", start, _ => false, fdeTasks, true);                                         // always on time
        await Log(fde, "karan", start, d => d == yesterday, fdeTasks[..2], true);                          // missed FDE yesterday...
        await Log(dev, "karan", devStart, _ => false, [("DevOps labs", [1.5, 2, 1.5, 2, 1])], true);       // ...but logged DevOps
        await Log(fde, "neha", start, d => d >= monday.AddDays(2) && d <= monday.AddDays(3) || d == start.AddDays(2), fdeTasks[..2], false); // gaps, never submits
        await Log(fde, "rahul", start, _ => false, [("Training theory", [2, 2, 1.5, 2, 2])], true);
        await Log(fde, "meera", monday.AddDays(2), _ => false, [("Onboarding and setup", [4, 3.5, 3]), ("Training theory", [2, 3, 3])], false);
        await Log(dev, "arjun", devStart, _ => false, [("Training theory", [3, 3, 2.5, 3, 3]), ("Lab: pipelines", [3, 2.5, 3, 3, 2])], true);
        for (var i = 0; i < 3; i++) // Vikram used the old daily form in week 1
            await s.Exec("insert into lp_daily_status (program_id, user_id, on_date, \"values\") values (@fde, @u, @d::date, @v::jsonb)",
                new { fde, u = ids["vikram"], d = Iso(start.AddDays(i)), v = Values("daily", i) });

        // ---- feed
        async Task<Guid> Post(Guid pid, string k, string body, string? url, int daysAgo) =>
            await s.Scalar<Guid>("insert into feed_posts (author_id, program_id, body, url, created_at) values (@u, @pid, @body, @url, now() - make_interval(days => @daysAgo, hours => @h)) returning id",
                new { u = ids[k], pid, body, url, daysAgo, h = daysAgo * 3 % 9 });
        var posts = new List<(Guid Id, Guid Program)>
        {
            (await Post(fde, "priya", "Welcome to the FDE program! Week 1 is done: LLM fundamentals to the end-to-end app. Post anything useful you find, and your demos.", null, 13), fde),
            (await Post(fde, "asha", "This talk on building effective agents made tool calling click for me.", "https://www.youtube.com/watch?v=D7_ipDqhtwk", 9), fde),
            (await Post(fde, "karan", "How I cut our RAG bot's token cost by 40% with smaller chunks and caching.", "https://www.anthropic.com/news/contextual-retrieval", 5), fde),
            (await Post(fde, "neha", "Anyone else stuck on schema validation retries? Validate, then retry once with the error in the prompt. Works for me.", null, 3), fde),
            (await Post(fde, "rahul", "Week 2 starts with RAG: ingestion, chunking, embeddings, then retrieval. Read the topics for Day 1 before you start.", null, 1), fde),
            (await Post(dev, "rahul", "Reminder: the incident drill is next Thursday. Read the runbook first.", "https://sre.google/sre-book/managing-incidents/", 6), dev),
            (await Post(dev, "karan", "Got our image from 1.1 GB to 180 MB with a multi-stage build.", "https://docs.docker.com/build/building/multi-stage/", 2), dev),
        };
        var fans = new[] { "asha", "karan", "neha", "rahul", "meera", "priya", "vikram" };
        for (var i = 0; i < posts.Count; i++)
            foreach (var k in fans.Skip(i % 3).Take(2 + i % 5))
                await s.Exec("insert into feed_likes (post_id, user_id) values (@p, @u) on conflict do nothing", new { p = posts[i].Id, u = ids[k] });
        async Task Comment(int post, string k, string body) => await s.Exec("insert into feed_comments (post_id, author_id, body) values (@p, @u, @body)", new { p = posts[post].Id, u = ids[k], body });
        await Comment(2, "priya", "Great write-up. Can you show it at demo day?");
        await Comment(2, "karan", "Sure, I will add the before/after numbers.");
        await Comment(3, "arjun", "Same pattern here, and log the failures so the evals catch them.");
        await Comment(4, "meera", "Thanks, I will read it tonight.");
        await Comment(6, "rahul", "Nice. Add it to the team wiki?");

        // ---- people from the training sheets: real names and their real status, in the demo FDE program only
        var sheetPeople = 0;
        if (sheet is { People.Count: > 0 })
        {
            var day1 = (real is { } rid ? await s.First<DateOnly?>("select starts_on from lp_programs where id = @rid", new { rid }) : null) ?? monday; // the sheet's "Day 1"
            DateOnly Workday(int n) { var d = day1; for (var c = 1; c < n;) { d = d.AddDays(1); if (!Weekend(d)) c++; } return d; }
            var planDays = await s.Query<DayRow>("select id, topic from lp_days where program_id = @fde order by week, day", new { fde });
            var dayFields = fields.Where(f => f.Form == "day").ToList();
            var dailyFields = fields.Where(f => f.Form == "daily").ToList();
            var n = 0;
            foreach (var person in sheet.People)
            {
                var key = Regex.Replace(person.Name.ToLowerInvariant(), "[^a-z]+", ".").Trim('.');
                if (key.Length == 0 || ids.ContainsKey(key)) continue;
                var uid = ids[key] = Guid.NewGuid();
                await s.Exec("insert into app_users (id, email, display_name, role, access_status) values (@uid, @email, @name, 'member', 'approved')",
                    new { uid, email = $"{key}.{++n}@{Domain}", name = Cut(person.Name, 80) });
                await s.Exec("insert into lp_members (program_id, user_id, role, added_at) values (@fde, @uid, 'member', @since::date)", new { fde, uid, since = Iso(day1 < start ? day1 : start) });
                sheetPeople++;

                for (var i = 0; i < person.Days.Count && i < planDays.Count; i++)
                {
                    var d = person.Days[i];
                    if (d.Learned.Length == 0 && d.Proof.Length == 0) continue;
                    int? rate = int.TryParse(d.Rate, out var rv) && rv is >= 1 and <= 5 ? rv : null;
                    var values = new Dictionary<string, object>();
                    foreach (var f in dayFields)
                    {
                        if (f.Type == "rating") { if (rate is { } rr) values[f.Key] = rr; }
                        else if (f.Key.Contains("proof")) { if (d.Proof.Length > 0) values[f.Key] = Cut(d.Proof, 4000); }
                        else if (d.Learned.Length > 0) values[f.Key] = Cut(d.Learned, 4000);
                    }
                    await s.Exec("""
                        insert into lp_day_status (program_id, user_id, day_id, "values", completed, completed_at)
                        values (@fde, @uid, @day, @v::jsonb, @c, case when @c then now() end) on conflict (user_id, day_id) do nothing
                        """, new { fde, uid, day = planDays[i].Id, v = JsonSerializer.Serialize(values), c = d.Learned.Length > 0 && rate is not null });
                }

                var byWeek = new Dictionary<DateOnly, List<(DateOnly Date, string Title, int Minutes, string Note)>>();
                foreach (var (dayN, text) in person.Daily)
                {
                    var date = Workday(dayN);
                    if (date > today) continue;
                    var (project, theory, practical) = ParseDaily(text);
                    var values = new Dictionary<string, object>();
                    foreach (var f in dailyFields)
                    {
                        var v = f.Key.Contains("project") ? (project.Length > 0 ? project : "None") : f.Key.Contains("theory") ? (theory.Length > 0 ? theory : "Not given") : (practical.Length > 0 ? practical : "Not given");
                        values[f.Key] = Cut(v, f.Type == "text" ? 500 : 4000);
                    }
                    await s.Exec("insert into lp_daily_status (program_id, user_id, on_date, \"values\") values (@fde, @uid, @d::date, @v::jsonb) on conflict do nothing",
                        new { fde, uid, d = Iso(date), v = JsonSerializer.Serialize(values) });
                    var wk = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
                    if (!byWeek.TryGetValue(wk, out var list)) byWeek[wk] = list = [];
                    if (project.Length > 0 && !Regex.IsMatch(project, @"^(none|not\b|leave|half-day leave)", RegexOptions.IgnoreCase)) list.Add((date, "Project work", Minutes(HoursIn(project), 2), project));
                    if (theory.Length > 0) list.Add((date, "Training theory", Minutes(HoursIn(theory), 3), theory));
                    if (practical.Length > 0) list.Add((date, "Practical task", Minutes(HoursIn(practical), 2), practical));
                }
                foreach (var (wk, cells) in byWeek)
                {
                    var pos = 0;
                    foreach (var g in cells.GroupBy(c => c.Title))
                    {
                        var tid = await s.Scalar<Guid>("insert into lp_time_tasks (program_id, user_id, week_start, title, position) values (@fde, @uid, @w::date, @t, @p) returning id", new { fde, uid, w = Iso(wk), t = g.Key, p = pos++ });
                        foreach (var c in g.GroupBy(x => x.Date).Select(x => x.First()))
                            await s.Exec("insert into lp_time_cells (task_id, on_date, minutes, note) values (@tid, @d::date, @m, @note)", new { tid, d = Iso(c.Date), m = Math.Clamp(c.Minutes, 0, 1440), note = Cut(c.Note, 500) });
                    }
                    await s.Exec("insert into lp_time_weeks (program_id, user_id, week_start) values (@fde, @uid, @w::date) on conflict do nothing", new { fde, uid, w = Iso(wk) });
                }
            }
        }

        // ---- reminders for yesterday (as the 10:00 job would have made them)
        await s.Exec("""
            insert into notifications (user_id, kind, program_id, on_date, title, body, link)
            select m.user_id, 'daily_missed', m.program_id, @y::date, 'Fill in your daily status for ' || p.name,
                   'You have not filled in your ' || p.name || ' daily status for ' || to_char(@y::date, 'Dy DD Mon') || '.', '/p/' || p.id || '/daily?date=' || @y
            from lp_members m join lp_programs p on p.id = m.program_id
            where p.is_demo and m.added_at::date <= @y::date
              and not exists (select 1 from lp_time_cells c join lp_time_tasks t on t.id = c.task_id where t.program_id = m.program_id and t.user_id = m.user_id and c.on_date = @y::date and c.minutes > 0)
              and not exists (select 1 from lp_daily_status x where x.program_id = m.program_id and x.user_id = m.user_id and x.on_date = @y::date)
            on conflict do nothing
            """, new { y = Iso(yesterday) });

        return new DemoStatus(true, 2, People.Length + sheetPeople);
    });

    static async Task Days(Session s, Guid pid, (string Topic, string Cover, string Task)[] plan)
    {
        for (var i = 0; i < plan.Length; i++)
            await s.Exec("insert into lp_days (program_id, week, day, topic, topics_to_cover, practical_task) values (@pid, @w, @d, @topic, @cover, @task)",
                new { pid, w = i / 5 + 1, d = i % 5 + 1, topic = plan[i].Topic, cover = plan[i].Cover, task = plan[i].Task });
    }

    static readonly string[] Notes = ["Read the docs and took notes", "Paired with a teammate", "Finished the exercises", "Reviewed my notes", "Wrote a summary"];
    static readonly string[] Projects = ["Acme support bot, 2 h", "None", "Estimator platform, 3 h", "Internal tools, 1 h"];
    static readonly string[] DayNotes =
    [
        "Tokens, context windows and sampling; compared three models on cost and latency.",
        "Few-shot prompts and prompt versioning; built a small prompt library with tests.",
        "JSON schemas and tool calling; validation with one retry on failure.",
        "Built a 30-case evaluation set and an LLM-as-judge check.",
        "Wired the pieces into one assistant with logging and guardrails.",
        "RAG basics: chunking, embeddings and retrieval with citations.",
    ];
    static readonly string[] DevNotes =
    [
        "Worked through the lab in the playground and wrote down every command I used.",
        "Broke the setup on purpose, read the logs and fixed it; the cause was a wrong file mode.",
        "Wrote the script with set -euo pipefail and tested the failure path as well as the happy path.",
        "Compared the before and after: the image went from 1.1 GB to under 200 MB.",
        "Got the pipeline green; the fix was a missing permission on the token.",
    ];
    static readonly (string, string, string)[] FdePlan =
    [
        ("LLM Fundamentals & Model Selection", "Tokens, context windows, sampling, cost and latency.", "Build a minimal LLM-backed endpoint."),
        ("Advanced Prompt Engineering", "Zero-, one- and few-shot prompting, role prompting, versioning.", "Build a candidate-analysis assistant."),
        ("Structured Outputs & Tool Calling", "JSON schemas, function calling, validation and retries.", "Build an AI task-management assistant."),
        ("LLM Evaluation, Reliability & Guardrails", "Quality criteria, eval sets, guardrails.", "Create a 30-case evaluation set."),
        ("End-to-End LLM Application", "Architecture of an LLM-powered application.", "Deliver an AI-powered recruitment assistant."),
        ("RAG Architecture", "Retrieval-augmented generation and when to use it.", "Build a basic RAG pipeline."),
        ("Chunking & Embeddings", "Chunking strategies and embedding models.", "Compare two chunking strategies."),
        ("Vector Search", "Vector stores, indexes and metadata filters.", "Retrieval with metadata filters."),
        ("RAG Quality Evaluation", "Retrieval versus generation failures.", "Evaluate the RAG bot on 20 questions."),
        ("RAG Application", "End-to-end document assistant.", "Deliver a document assistant."),
    ];
    static readonly (string, string, string)[] DevPlan =
    [
        ("Linux & Shell Essentials", "Processes, permissions, systemd, bash.", "Script a log-rotation job."),
        ("Git Workflows", "Branching, PRs, protected branches.", "Branch protection and a PR template."),
        ("CI with GitHub Actions", "Workflows, caching, secrets.", "CI pipeline with tests and lint."),
        ("Docker Deep Dive", "Images, layers, multi-stage builds.", "Slim a 1 GB image below 200 MB."),
        ("Kubernetes Basics", "Pods, deployments, services.", "Deploy the app to kind."),
    ];
}
