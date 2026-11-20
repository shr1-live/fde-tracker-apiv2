using System.Globalization;
using System.Text.Json.Nodes;
using ClosedXML.Excel;

namespace FdeTracker.Tests;

/// <summary>Programs V3 (docs/PROGRAMS-V3.md): invite-only sign-in, per-program roles, plan and forms, status rules, reports, Excel exports and the feed.
/// Its own database. Co is the FDE coordinator, M1 and M2 are FDE members, Dx coordinates a second program, Late signs in before being invited.</summary>
[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class ProgramsV3Tests(ApiFixture api) : IClassFixture<ApiFixture>
{
    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser Co = new(ApiFixture.Id(401), "coord@fiftyfivetech.io");
    static readonly TestUser M1 = new(ApiFixture.Id(402), "member.one@fiftyfivetech.io");
    static readonly TestUser M2 = new(ApiFixture.Id(403), "member.two@fiftyfivetech.io");
    static readonly TestUser Dx = new(ApiFixture.Id(404), "devops.lead@fiftyfivetech.io");
    static readonly TestUser Late = new(ApiFixture.Id(405), "late@fiftyfivetech.io");
    static readonly TestUser Legacy = new(ApiFixture.Id(406), "legacy@fiftyfivetech.io");
    static readonly TestUser Nobody = new(ApiFixture.Id(407), "nobody@fiftyfivetech.io");
    static readonly TestUser Barred = new(ApiFixture.Id(408), "barred@fiftyfivetech.io");

    // Shared between the ordered steps (xUnit makes a new instance per test).
    static string Fde = "", DevOps = "", Day1 = "", Day2 = "", DevOpsDay = "";

    static readonly int DarkBlue = System.Drawing.Color.FromArgb(0x1F, 0x38, 0x64).ToArgb();

    static string P(string id) => $"/api/programs/{id}";
    static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static string DaysFromToday(int n) => DateTime.UtcNow.AddDays(n).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    async Task<int> Status(string method, string path, TestUser user, object? body = null) => (await api.Call(method, path, user, body)).Status;

    static object DailyValues(string project = "None", string theory = "Tokens and context windows, 3 h", string practical = "Endpoint built") =>
        new { values = new { project, theory, practical } };

    [Fact]
    public async Task V01_sign_in_is_invite_only_and_invites_become_memberships()
    {
        var me = (await api.Call("GET", "/api/me", Admin)).Body;
        Assert.Equal("admin", (string)me["role"]!);
        Assert.True((bool)me["can_manage"]!);
        var fde = me["programs"]!.AsArray().Single(p => (string)p!["name"]! == "FDE Training Program")!;
        Assert.Equal("admin", (string)fde["role"]!);
        Fde = (string)fde["id"]!;

        // Unknown email: pending, no programs, refused everywhere that needs approval.
        var late = (await api.Call("GET", "/api/me", Late)).Body;
        Assert.Equal("pending", (string)late["access"]!);
        Assert.Empty(late["programs"]!.AsArray());
        Assert.False((bool)late["can_manage"]!);
        Assert.Equal(403, await Status("GET", "/api/programs", Late));
        Assert.Equal(403, await Status("GET", "/api/feed", Late));

        // Invites before the first sign-in.
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(Fde)}/invites", Admin, new { email = Co.Email, role = "coordinator" })).Body["status"]!);
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(Fde)}/invites", Admin, new { email = "MEMBER.ONE@fiftyfivetech.io", role = "member" })).Body["status"]!);
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(Fde)}/invites", Admin, new { email = M2.Email, role = "member" })).Body["status"]!);
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/invites", Admin, new { email = "someone@gmail.com", role = "member" }));
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/invites", Admin, new { email = M2.Email, role = "owner" }));
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/invites", Admin, new { email = "not-an-email", role = "member" }));

        var co = (await api.Call("GET", "/api/me", Co)).Body;
        Assert.Equal("approved", (string)co["access"]!);
        Assert.Equal("coordinator", (string)co["programs"]![0]!["role"]!);
        Assert.True((bool)co["can_manage"]!);
        var m1 = (await api.Call("GET", "/api/me", M1)).Body;
        Assert.Equal("approved", (string)m1["access"]!);
        Assert.Equal("member", (string)m1["programs"]![0]!["role"]!);
        Assert.False((bool)m1["can_manage"]!);
        Assert.Equal("approved", (string)(await api.Call("GET", "/api/me", M2)).Body["access"]!);
        Assert.Equal(1, await api.Count("select count(*) from lp_invites where email = 'member.one@fiftyfivetech.io' and accepted_at is not null"));

        // Someone approved before V3 stays approved without an invite.
        await api.Count($"with x as (insert into app_users (id, email, role, access_status) values ('{Legacy.Id}', '{Legacy.Email}', 'member', 'approved') returning 1) select count(*) from x");
        Assert.Equal("approved", (string)(await api.Call("GET", "/api/me", Legacy)).Body["access"]!);

        // A person who already signed in is added at once and approved.
        Assert.Equal("added", (string)(await api.Call("POST", $"{P(Fde)}/invites", Co, new { email = Late.Email, role = "member" })).Body["status"]!);
        late = (await api.Call("GET", "/api/me", Late)).Body;
        Assert.Equal("approved", (string)late["access"]!);
        Assert.Equal("member", (string)late["programs"]![0]!["role"]!);

        // The admin stays admin.
        Assert.Equal("admin", (string)(await api.Call("GET", "/api/me", Admin)).Body["role"]!);
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/invites", Co, new { email = Admin.Email, role = "member" }));

        // A coordinator's invite never lifts the admin's "rejected" (review finding): added to the program, still locked out.
        Assert.Equal("pending", (string)(await api.Call("GET", "/api/me", Barred)).Body["access"]!);
        Assert.Equal(200, await Status("POST", $"/api/admin/users/{Barred.Id}/access", Admin, new { status = "rejected" }));
        Assert.Equal("added", (string)(await api.Call("POST", $"{P(Fde)}/invites", Co, new { email = Barred.Email, role = "member" })).Body["status"]!);
        Assert.Equal("rejected", (string)(await api.Call("GET", "/api/me", Barred)).Body["access"]!);
        Assert.Equal(403, await Status("GET", "/api/feed", Barred));
    }

    [Fact]
    public async Task V02_the_seeded_program_has_week_1_and_the_default_forms()
    {
        var list = (await api.Call("GET", "/api/programs", M1)).Body.AsArray();
        var summary = Assert.Single(list)!;
        Assert.Equal("member", (string)summary["my_role"]!);
        Assert.Equal("2026-11-16", (string)summary["starts_on"]!);
        Assert.True((int)summary["member_count"]! >= 4);

        var p = (await api.Call("GET", P(Fde), M1)).Body;
        Assert.Equal("member", (string)p["my_role"]!);
        var days = p["days"]!.AsArray();
        Assert.Equal(5, days.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, days.Select(d => (int)d!["day"]!).ToArray());
        Assert.All(days, d => Assert.Equal(1, (int)d!["week"]!));
        Assert.Equal("LLM Fundamentals & Model Selection", (string)days[0]!["topic"]!);
        Assert.StartsWith("• LLM architecture", (string)days[0]!["topics_to_cover"]!);
        Assert.Contains("Acceptance criteria:", (string)days[4]!["practical_task"]!);
        Assert.Null(days[0]!["resources"]);
        Day1 = (string)days[0]!["id"]!;
        Day2 = (string)days[1]!["id"]!;

        var daily = p["forms"]!["daily"]!.AsArray();
        Assert.Equal(new[] { "project", "theory", "practical" }, daily.Select(f => (string)f!["key"]!).ToArray());
        Assert.All(daily, f => Assert.True((bool)f!["required"]!));
        Assert.Equal("text", (string)daily[0]!["type"]!);
        var day = p["forms"]!["day"]!.AsArray();
        Assert.Equal(new[] { "learned", "proof", "rating" }, day.Select(f => (string)f!["key"]!).ToArray());
        Assert.Equal(new[] { true, false, true }, day.Select(f => (bool)f!["required"]!).ToArray());
        Assert.Equal("rating", (string)day[2]!["type"]!);
        Assert.StartsWith("1 = Needs significant guidance", (string)day[2]!["description"]!);
    }

    [Fact]
    public async Task V03_only_the_admin_creates_programs_and_only_coordinators_change_one()
    {
        Assert.Equal(403, await Status("POST", "/api/programs", Co, new { name = "DevOps Program" }));
        Assert.Equal(403, await Status("POST", "/api/programs", M1, new { name = "DevOps Program" }));
        Assert.Equal(400, await Status("POST", "/api/programs", Admin, new { name = "D" }));
        Assert.Equal(400, await Status("POST", "/api/programs", Admin, new { name = "Dated", starts_on = "2026-02-30" }));
        var created = await api.Call("POST", "/api/programs", Admin, new { name = "DevOps Program", description = "Pipelines and clouds", starts_on = "2026-12-01" });
        Assert.Equal(201, created.Status);
        Assert.Equal("admin", (string)created.Body["my_role"]!);
        Assert.Equal(0, (int)created.Body["member_count"]!);
        DevOps = (string)created.Body["id"]!;
        Assert.Equal(409, await Status("POST", "/api/programs", Admin, new { name = "DevOps Program" }));
        Assert.Equal(3, (await api.Call("GET", P(DevOps), Admin)).Body["forms"]!["daily"]!.AsArray().Count); // new programs start with the default forms

        Assert.Equal(403, await Status("PATCH", P(Fde), M1, new { description = "hijack" }));
        var patched = await api.Call("PATCH", P(Fde), Co, new { description = "Updated by the coordinator" });
        Assert.Equal(200, patched.Status);
        Assert.Equal("Updated by the coordinator", (string)patched.Body["description"]!);
        Assert.Equal("FDE Training Program", (string)patched.Body["name"]!);
        Assert.Equal(409, await Status("PATCH", P(Fde), Co, new { name = "DevOps Program" }));
        Assert.Equal(200, await Status("PATCH", P(Fde), Co, new { description = (string?)null }));
        Assert.Null((await api.Call("GET", P(Fde), Co)).Body["description"]);
        Assert.Equal(404, await Status("PATCH", P(DevOps), Co, new { description = "not mine" }));
        Assert.Equal(404, await Status("GET", P(Guid.NewGuid().ToString()), Admin));

        // days
        var newDay = new { week = 2, day = 1, topic = "RAG basics", topics_to_cover = "• Chunking" };
        Assert.Equal(403, await Status("POST", $"{P(Fde)}/days", M1, newDay));
        var day = await api.Call("POST", $"{P(Fde)}/days", Co, newDay);
        Assert.Equal(201, day.Status);
        Assert.Null(day.Body["practical_task"]);
        var dayId = (string)day.Body["id"]!;
        Assert.Equal(409, await Status("POST", $"{P(Fde)}/days", Co, newDay));
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/days", Co, new { week = 53, day = 1, topic = "x", topics_to_cover = "" }));
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/days", Co, new { week = 2, day = 8, topic = "x", topics_to_cover = "" }));
        Assert.Equal(400, await Status("POST", $"{P(Fde)}/days", Co, new { week = 2, day = 2, topic = "", topics_to_cover = "" }));
        Assert.Equal(403, await Status("PATCH", $"{P(Fde)}/days/{dayId}", M1, new { topic = "x" }));
        var renamed = await api.Call("PATCH", $"{P(Fde)}/days/{dayId}", Co, new { topic = "RAG in depth", resources = "https://example.com/rag" });
        Assert.Equal(200, renamed.Status);
        Assert.Equal("RAG in depth", (string)renamed.Body["topic"]!);
        Assert.Equal("• Chunking", (string)renamed.Body["topics_to_cover"]!);
        Assert.Equal(409, await Status("PATCH", $"{P(Fde)}/days/{dayId}", Co, new { week = 1, day = 1 }));
        Assert.Equal(403, await Status("DELETE", $"{P(Fde)}/days/{dayId}", M1));
        Assert.Equal(204, await Status("DELETE", $"{P(Fde)}/days/{dayId}", Co));
        Assert.Equal(404, await Status("DELETE", $"{P(Fde)}/days/{dayId}", Co));
        Assert.Equal(5, (await api.Call("GET", P(Fde), M1)).Body["days"]!.AsArray().Count);

        // forms (on the second program, so the FDE forms stay the defaults)
        object F(string key, string type = "text", bool required = false, string label = "Label") => new { key, label, description = (string?)null, type, required };
        Assert.Equal(403, await Status("PUT", $"{P(Fde)}/forms/daily", M1, new { fields = new[] { F("abc") } }));
        Assert.Equal(404, await Status("PUT", $"{P(Fde)}/forms/weekly", Co, new { fields = new[] { F("abc") } }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = Array.Empty<object>() }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = Enumerable.Range(0, 21).Select(i => F($"f{i:D2}")).ToArray() }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = new[] { F("dup"), F("dup") } }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = new[] { F("Bad-Key") } }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = new[] { F("abc", type: "date") } }));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/forms/daily", Admin, new { fields = new[] { F("abc", label: "") } }));
        var saved = await api.Call("PUT", $"{P(DevOps)}/forms/daily", Admin, new
        {
            fields = new[] { F("summary", "longtext", true, "Summary"), F("hours", "number", false, "Hours"), F("link", "url", false, "Link"), F("mood", "rating", false, "Mood") },
        });
        Assert.Equal(200, saved.Status);
        Assert.Equal(new[] { "summary", "hours", "link", "mood" }, saved.Body.AsArray().Select(f => (string)f!["key"]!).ToArray());
        Assert.Equal(new[] { "summary", "hours", "link", "mood" }, (await api.Call("GET", P(DevOps), Admin)).Body["forms"]!["daily"]!.AsArray().Select(f => (string)f!["key"]!).ToArray()); // replaced, in order

        // members and invites
        Assert.Equal(403, await Status("GET", $"{P(Fde)}/members", M1));
        var members = (await api.Call("GET", $"{P(Fde)}/members", Co)).Body;
        Assert.Contains(members["members"]!.AsArray(), m => (string)m!["email"]! == M1.Email && (string)m["role"]! == "member" && (string)m["name"]! == "member.one");
        Assert.Equal(403, await Status("POST", $"{P(Fde)}/invites", M1, new { email = "friend@fiftyfivetech.io", role = "member" }));
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(Fde)}/invites", Co, new { email = "friend@fiftyfivetech.io", role = "member" })).Body["status"]!);
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(Fde)}/invites", Co, new { email = "friend@fiftyfivetech.io", role = "coordinator" })).Body["status"]!);
        var invite = (await api.Call("GET", $"{P(Fde)}/members", Co)).Body["invites"]!.AsArray().Single(i => (string)i!["email"]! == "friend@fiftyfivetech.io")!;
        Assert.Equal("coordinator", (string)invite["role"]!); // re-inviting updates the role
        Assert.Equal(403, await Status("DELETE", $"{P(Fde)}/invites/{(string)invite["id"]!}", M1));
        Assert.Equal(204, await Status("DELETE", $"{P(Fde)}/invites/{(string)invite["id"]!}", Co));
        Assert.Equal(404, await Status("DELETE", $"{P(Fde)}/invites/{(string)invite["id"]!}", Co));

        Assert.Equal(403, await Status("PATCH", $"{P(Fde)}/members/{M2.Id}", M1, new { role = "coordinator" }));
        Assert.Equal(400, await Status("PATCH", $"{P(Fde)}/members/{Co.Id}", Co, new { role = "member" })); // not yourself
        Assert.Equal(400, await Status("PATCH", $"{P(Fde)}/members/{M2.Id}", Co, new { role = "admin" }));
        var promoted = await api.Call("PATCH", $"{P(Fde)}/members/{M2.Id}", Co, new { role = "coordinator" });
        Assert.Equal("coordinator", (string)promoted.Body["role"]!);
        Assert.Equal(200, await Status("PATCH", $"{P(Fde)}/members/{M2.Id}", Admin, new { role = "member" }));
        Assert.Equal(404, await Status("PATCH", $"{P(Fde)}/members/{Nobody.Id}", Co, new { role = "member" }));
        Assert.Equal(400, await Status("DELETE", $"{P(Fde)}/members/{Co.Id}", Co));
        Assert.Equal(400, await Status("DELETE", $"{P(Fde)}/members/{Admin.Id}", Co));
        Assert.Equal(403, await Status("DELETE", $"{P(Fde)}/members/{Late.Id}", M1));
        Assert.Equal(204, await Status("DELETE", $"{P(Fde)}/members/{Late.Id}", Co));
        Assert.Equal(404, await Status("GET", P(Fde), Late)); // out of the program: it is invisible
        Assert.Equal(404, await Status("PUT", $"{P(Fde)}/daily/{Today}", Late, DailyValues()));
    }

    [Fact]
    public async Task V04_a_coordinator_of_one_program_cannot_read_or_change_another()
    {
        Assert.Equal("invited", (string)(await api.Call("POST", $"{P(DevOps)}/invites", Admin, new { email = Dx.Email, role = "coordinator" })).Body["status"]!);
        var me = (await api.Call("GET", "/api/me", Dx)).Body;
        Assert.Equal(DevOps, (string)Assert.Single(me["programs"]!.AsArray())!["id"]!);

        foreach (var (method, path, body) in new (string, string, object?)[]
        {
            ("GET", P(Fde), null), ("PATCH", P(Fde), new { description = "x" }), ("POST", $"{P(Fde)}/days", new { week = 3, day = 1, topic = "x", topics_to_cover = "" }),
            ("GET", $"{P(Fde)}/members", null), ("POST", $"{P(Fde)}/invites", new { email = "x@fiftyfivetech.io", role = "member" }),
            ("GET", $"{P(Fde)}/daily", null), ("GET", $"{P(Fde)}/day-status", null), ("PUT", $"{P(Fde)}/daily/{Today}", DailyValues()),
            ("GET", $"{P(Fde)}/reports/daily", null), ("GET", $"{P(Fde)}/reports/program", null), ("GET", $"{P(Fde)}/reports/program.xlsx", null),
            ("PATCH", $"{P(Fde)}/members/{M1.Id}", new { role = "coordinator" }), ("DELETE", $"{P(Fde)}/members/{M1.Id}", null),
        })
            Assert.Equal(404, await Status(method, path, Dx, body));

        Assert.Equal(404, await Status("GET", P(DevOps), Co));
        Assert.Equal(404, await Status("GET", $"{P(DevOps)}/members", Co));
        Assert.Equal(404, await Status("PUT", $"{P(DevOps)}/forms/day", Co, new { fields = new[] { new { key = "abc", label = "A", description = (string?)null, type = "text", required = false } } }));
        Assert.DoesNotContain((await api.Call("GET", "/api/programs", Co)).Body.AsArray(), p => (string)p!["id"]! == DevOps);
        Assert.Equal(2, (await api.Call("GET", "/api/programs", Admin)).Body.AsArray().Count);

        var d = await api.Call("POST", $"{P(DevOps)}/days", Dx, new { week = 1, day = 1, topic = "Pipelines", topics_to_cover = "• CI" });
        Assert.Equal(201, d.Status);
        DevOpsDay = (string)d.Body["id"]!;
        Assert.Equal(404, await Status("PUT", $"{P(Fde)}/day-status/{DevOpsDay}", M1, new { values = new { }, completed = false })); // another program's day
    }

    [Fact]
    public async Task V05_daily_values_are_checked_against_the_form()
    {
        var ok = await api.Call("PUT", $"{P(Fde)}/daily/{Today}", M1, DailyValues(project: "  Acme portal, 2 h  "));
        Assert.Equal(200, ok.Status);
        Assert.Equal("Acme portal, 2 h", (string)ok.Body["values"]!["project"]!);
        Assert.Equal(Today, (string)ok.Body["on_date"]!);
        Assert.Equal(M1.Id.ToString(), (string)ok.Body["user_id"]!);

        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, new { values = new { project = "x", theory = "x", practical = "x", extra = "x" } })); // unknown key
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, DailyValues(theory: "   "))); // empty = missing, and it is required
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, new { values = new { project = "x", theory = "x" } }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, DailyValues(project: new string('a', 501))));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, DailyValues(theory: new string('a', 4001))));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, new { values = new { project = 5, theory = "x", practical = "x" } }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{Today}", M1, new { values = "not an object" }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/{DaysFromToday(2)}", M1, DailyValues())); // future
        Assert.Equal(200, await Status("PUT", $"{P(Fde)}/daily/{DaysFromToday(1)}", M1, DailyValues())); // tomorrow is fine
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/2019-12-31", M1, DailyValues()));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/2026-13-01", M1, DailyValues()));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/daily/yesterday", M1, DailyValues()));
        Assert.Equal(200, await Status("PUT", $"{P(Fde)}/daily/2026-01-05", M1, DailyValues(project: "Day one project")));
        Assert.Equal(200, await Status("PUT", $"{P(Fde)}/daily/2026-01-06", M2, DailyValues(project: "M2 project")));

        // number, url and rating fields (the DevOps form)
        object V(object values) => new { values };
        Assert.Equal(200, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", hours = 7.5, link = "https://github.com/x/y", mood = 4 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", link = "http://github.com/x" })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", link = "javascript:alert(1)" })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", link = "https://" + new string('a', 500) })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", hours = 1001 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", hours = -1 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", mood = 6 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", mood = 0 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", mood = 2.5 })));
        Assert.Equal(400, await Status("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", mood = "great" })));
        var stored = await api.Call("PUT", $"{P(DevOps)}/daily/{Today}", Dx, V(new { summary = "ok", link = "", mood = 5 }));
        Assert.Equal(200, stored.Status);
        Assert.Null(stored.Body["values"]!["link"]); // empty values are not stored
        Assert.Equal(5, (int)stored.Body["values"]!["mood"]!);
    }

    [Fact]
    public async Task V06_a_day_needs_the_required_fields_only_when_it_is_marked_completed()
    {
        var draft = await api.Call("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { }, completed = false });
        Assert.Equal(200, draft.Status);
        Assert.False((bool)draft.Body["completed"]!);
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { proof = "https://github.com/me/day1" }, completed = true }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { learned = "Tokens", rating = 6 }, completed = true }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { learned = "Tokens", rating = 3, mood = 1 }, completed = true }));
        Assert.Equal(400, await Status("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { learned = "Tokens" } })); // completed must be sent
        var done = await api.Call("PUT", $"{P(Fde)}/day-status/{Day1}", M1, new { values = new { learned = "Tokens and context windows", proof = "", rating = 4 }, completed = true });
        Assert.Equal(200, done.Status);
        Assert.True((bool)done.Body["completed"]!);
        Assert.Equal(Day1, (string)done.Body["day_id"]!);
        Assert.Null(done.Body["values"]!["proof"]);
        Assert.Equal(1, await api.Count($"select count(*) from lp_day_status where day_id = '{Day1}' and completed and completed_at is not null"));
        Assert.Equal(200, await Status("PUT", $"{P(Fde)}/day-status/{Day2}", M1, new { values = new { learned = "Few-shot" }, completed = false }));
        Assert.Equal(404, await Status("PUT", $"{P(Fde)}/day-status/{Guid.NewGuid()}", M1, new { values = new { }, completed = false }));
    }

    [Fact]
    public async Task V07_members_see_only_their_own_status_and_coordinators_see_everyone()
    {
        var mine = (await api.Call("GET", $"{P(Fde)}/daily", M1)).Body.AsArray();
        Assert.NotEmpty(mine);
        Assert.All(mine, e => Assert.Equal(M1.Id.ToString(), (string)e!["user_id"]!));
        Assert.Equal(200, await Status("GET", $"{P(Fde)}/daily?user_id={M1.Id}", M1));
        Assert.Equal(403, await Status("GET", $"{P(Fde)}/daily?user_id={M2.Id}", M1));
        Assert.Equal(403, await Status("GET", $"{P(Fde)}/day-status?user_id={M2.Id}", M1));
        Assert.Equal(400, await Status("GET", $"{P(Fde)}/daily?from=last-week", M1));
        var ranged = (await api.Call("GET", $"{P(Fde)}/daily?from=2026-01-05&to=2026-01-05", M1)).Body.AsArray();
        Assert.Equal("2026-01-05", (string)Assert.Single(ranged)!["on_date"]!);

        foreach (var boss in new[] { Co, Admin })
        {
            var all = (await api.Call("GET", $"{P(Fde)}/daily", boss)).Body.AsArray();
            Assert.Contains(all, e => (string)e!["user_id"]! == M2.Id.ToString());
            Assert.Contains(all, e => (string)e!["user_id"]! == M1.Id.ToString());
            Assert.All((await api.Call("GET", $"{P(Fde)}/daily?user_id={M2.Id}", boss)).Body.AsArray(), e => Assert.Equal(M2.Id.ToString(), (string)e!["user_id"]!));
            Assert.Equal(2, (await api.Call("GET", $"{P(Fde)}/day-status?user_id={M1.Id}", boss)).Body.AsArray().Count);
        }

        // reports
        var own = (await api.Call("GET", $"{P(Fde)}/reports/daily?from=2026-01-05&to=2026-01-11", M1)).Body;
        Assert.Equal(M1.Id.ToString(), (string)Assert.Single(own["people"]!.AsArray())!["user_id"]!);
        Assert.Equal(new[] { "2026-01-05", "2026-01-06", "2026-01-07", "2026-01-08", "2026-01-09" }, own["dates"]!.AsArray().Select(d => (string)d!).ToArray()); // working days
        Assert.Equal(3, own["fields"]!.AsArray().Count);
        Assert.Single(own["cells"]!.AsArray());
        var everyone = (await api.Call("GET", $"{P(Fde)}/reports/daily?from=2026-01-05&to=2026-01-11", Co)).Body;
        Assert.Equal(2, everyone["cells"]!.AsArray().Count);
        Assert.Contains(everyone["people"]!.AsArray(), p => (string)p!["user_id"]! == M2.Id.ToString());
        Assert.Equal(200, await Status("GET", $"{P(Fde)}/reports/daily", M1)); // default range
        Assert.Equal(400, await Status("GET", $"{P(Fde)}/reports/daily?from=2026-01-01&to=2026-06-01", Co)); // over 120 days
        Assert.Equal(400, await Status("GET", $"{P(Fde)}/reports/daily?from=2026-02-01&to=2026-01-01", Co));

        var prog = (await api.Call("GET", $"{P(Fde)}/reports/program", M1)).Body;
        Assert.Equal(5, prog["days"]!.AsArray().Count);
        Assert.Equal(3, prog["fields"]!.AsArray().Count);
        var person = Assert.Single(prog["people"]!.AsArray())!;
        Assert.Equal(5, person["rows"]!.AsArray().Count);
        Assert.True((bool)person["rows"]![0]!["completed"]!);
        Assert.Equal(4, (int)person["rows"]![0]!["values"]!["rating"]!);
        Assert.True((await api.Call("GET", $"{P(Fde)}/reports/program", Co)).Body["people"]!.AsArray().Count >= 3);
    }

    [Fact]
    public async Task V08_both_excel_exports_are_real_workbooks_in_the_sheet_layout()
    {
        Assert.Equal(200, await Status("PATCH", P(Fde), Co, new { starts_on = "2026-01-05" })); // a Monday: Day 1

        var daily = await api.Download($"{P(Fde)}/reports/daily.xlsx?from=2026-01-05&to=2026-01-16", Co);
        Assert.Equal(200, daily.Status);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", daily.ContentType);
        Assert.Contains("fde-training-program-daily-2026-01-05-2026-01-16.xlsx", daily.Disposition);
        using (var wb = new XLWorkbook(new MemoryStream(daily.Body)))
        {
            Assert.Equal(new[] { "Summary", "Status" }, wb.Worksheets.Select(w => w.Name).ToArray());
            var summary = wb.Worksheet("Summary");
            Assert.Equal("Field", summary.Cell(1, 2).GetString());
            Assert.Equal("Project worked", summary.Cell(2, 2).GetString());
            var status = wb.Worksheet("Status");
            var header = Enumerable.Range(1, 13).Select(c => status.Cell(1, c).GetString()).ToArray();
            Assert.Equal(new[] { "Name", "Day 1", "Day 2", "Day 3", "Day 4", "Day 5", "Weekly rating", "Day 6", "Day 7", "Day 8", "Day 9", "Day 10", "Weekly rating" }, header);
            Assert.Equal(DarkBlue, status.Cell(1, 2).Style.Fill.BackgroundColor.Color.ToArgb());
            Assert.True(status.Cell(1, 2).Style.Font.Bold);
            Assert.Equal(System.Drawing.Color.White.ToArgb(), status.Cell(1, 2).Style.Font.FontColor.Color.ToArgb());
            var m1Row = Enumerable.Range(2, 10).First(r => status.Cell(r, 1).GetString() == "member.one");
            Assert.Equal("Project worked: Day one project\nTraining theory: Tokens and context windows, 3 h\nPractical: Endpoint built", status.Cell(m1Row, 2).GetString());
            var m2Row = Enumerable.Range(2, 10).First(r => status.Cell(r, 1).GetString() == "member.two");
            Assert.StartsWith("Project worked: M2 project", status.Cell(m2Row, 3).GetString());
        }

        var program = await api.Download($"{P(Fde)}/reports/program.xlsx", Co);
        Assert.Equal(200, program.Status);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", program.ContentType);
        Assert.Contains("fde-training-program-status-report.xlsx", program.Disposition);
        using (var wb = new XLWorkbook(new MemoryStream(program.Body)))
        {
            var names = wb.Worksheets.Select(w => w.Name).ToArray();
            Assert.Equal("Readiness Guide", names[0]);
            Assert.Contains("member.one", names);
            Assert.Contains("member.two", names);
            Assert.StartsWith("Needs significant guidance", wb.Worksheet("Readiness Guide").Cell(2, 2).GetString());
            var ws = wb.Worksheet("member.one");
            var header = Enumerable.Range(1, 8).Select(c => ws.Cell(1, c).GetString()).ToArray();
            Assert.Equal(new[] { "Week", "Topic", "Topics to Cover", "Practical Task", "What You Learn", "Proof of Evidence", "Rate (1-5)", "Completed" }, header);
            Assert.Equal(DarkBlue, ws.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb());
            Assert.True(ws.Cell(1, 1).Style.Alignment.WrapText);
            Assert.Equal("Week 1", ws.Cell(2, 1).GetString());
            Assert.Equal("", ws.Cell(3, 1).GetString());
            Assert.Contains("LLM Fundamentals & Model Selection", ws.Cell(2, 2).GetString());
            Assert.Equal("Tokens and context windows", ws.Cell(2, 5).GetString());
            Assert.Equal(4, ws.Cell(2, 7).GetDouble());
            Assert.Equal("Yes", ws.Cell(2, 8).GetString());
            Assert.Equal("No", ws.Cell(3, 8).GetString());
        }

        var own = await api.Download($"{P(Fde)}/reports/program.xlsx", M1);
        using (var wb = new XLWorkbook(new MemoryStream(own.Body)))
            Assert.Equal(new[] { "Readiness Guide", "member.one" }, wb.Worksheets.Select(w => w.Name).ToArray()); // a member exports only themselves
        Assert.Equal(400, (await api.Download($"{P(Fde)}/reports/daily.xlsx?from=2026-01-01&to=2026-12-31", Co)).Status);
        Assert.Equal(404, (await api.Download($"{P(Fde)}/reports/daily.xlsx", Late)).Status);
        Assert.Equal(401, (await api.Download($"{P(Fde)}/reports/program.xlsx", null)).Status);
    }

    [Fact]
    public async Task V09_feed_posts_likes_and_comments_with_owner_or_admin_deletes()
    {
        Assert.Equal("pending", (string)(await api.Call("GET", "/api/me", Nobody)).Body["access"]!);
        Assert.Equal(403, await Status("POST", "/api/feed", Nobody, new { body = "hi" }));

        Assert.Equal(400, await Status("POST", "/api/feed", M1, new { }));
        Assert.Equal(400, await Status("POST", "/api/feed", M1, new { body = "   " }));
        Assert.Equal(400, await Status("POST", "/api/feed", M1, new { url = "http://example.com" }));
        Assert.Equal(400, await Status("POST", "/api/feed", M1, new { body = new string('a', 2001) }));
        var post = await api.Call("POST", "/api/feed", M1, new { body = "Finished Day 1", url = "https://github.com/me/day1" });
        Assert.Equal(201, post.Status);
        Assert.Equal("member.one", (string)post.Body["author"]!["name"]!);
        Assert.Equal(0, (int)post.Body["like_count"]!);
        Assert.Empty(post.Body["comments"]!.AsArray());
        var postId = (string)post.Body["id"]!;
        var linkOnly = await api.Call("POST", "/api/feed", M2, new { url = "https://example.com/read" });
        Assert.Equal(201, linkOnly.Status);
        Assert.Null(linkOnly.Body["body"]);

        var like = (await api.Call("POST", $"/api/feed/{postId}/like", M2)).Body;
        Assert.Equal(1, (int)like["like_count"]!);
        Assert.True((bool)like["liked_by_me"]!);
        like = (await api.Call("POST", $"/api/feed/{postId}/like", M2)).Body;
        Assert.Equal(0, (int)like["like_count"]!);
        Assert.False((bool)like["liked_by_me"]!);
        Assert.Equal(1, (int)(await api.Call("POST", $"/api/feed/{postId}/like", M2)).Body["like_count"]!);
        Assert.Equal(404, await Status("POST", $"/api/feed/{Guid.NewGuid()}/like", M2));

        var comment = await api.Call("POST", $"/api/feed/{postId}/comments", M2, new { body = "Nice work" });
        Assert.Equal(201, comment.Status);
        Assert.Equal("member.two", (string)comment.Body["author"]!["name"]!);
        var commentId = (string)comment.Body["id"]!;
        Assert.Equal(400, await Status("POST", $"/api/feed/{postId}/comments", M2, new { body = "" }));
        Assert.Equal(400, await Status("POST", $"/api/feed/{postId}/comments", M2, new { body = new string('a', 1001) }));
        Assert.Equal(404, await Status("POST", $"/api/feed/{Guid.NewGuid()}/comments", M2, new { body = "hello" }));

        var page = (await api.Call("GET", "/api/feed", M2)).Body["items"]!.AsArray();
        Assert.Equal((string)linkOnly.Body["id"]!, (string)page[0]!["id"]!); // newest first
        var p = page.Single(x => (string)x!["id"]! == postId)!;
        Assert.True((bool)p["liked_by_me"]!);
        Assert.Equal(1, (int)p["comment_count"]!);
        Assert.Equal("Nice work", (string)p["comments"]![0]!["body"]!);
        Assert.False((bool)(await api.Call("GET", "/api/feed", M1)).Body["items"]!.AsArray().Single(x => (string)x!["id"]! == postId)!["liked_by_me"]!);
        var first = (await api.Call("GET", "/api/feed?limit=1", M2)).Body;
        var older = (await api.Call("GET", $"/api/feed?limit=1&cursor={(string)first["next_cursor"]!}", M2)).Body["items"]!.AsArray();
        Assert.Equal(postId, (string)Assert.Single(older)!["id"]!);
        Assert.Equal(400, await Status("GET", "/api/feed?limit=51", M2));
        Assert.Equal(400, await Status("GET", "/api/feed?limit=0", M2));
        Assert.Equal(400, await Status("GET", "/api/feed?cursor=garbage", M2));

        Assert.Equal(403, await Status("DELETE", $"/api/feed/comments/{commentId}", M1)); // not the author (even as the post's author)
        Assert.Equal(204, await Status("DELETE", $"/api/feed/comments/{commentId}", M2));
        Assert.Equal(404, await Status("DELETE", $"/api/feed/comments/{commentId}", M2));
        var other = (string)(await api.Call("POST", $"/api/feed/{postId}/comments", M2, new { body = "Again" })).Body["id"]!;
        Assert.Equal(204, await Status("DELETE", $"/api/feed/comments/{other}", Admin));
        Assert.Equal(403, await Status("DELETE", $"/api/feed/{postId}", M2));
        Assert.Equal(403, await Status("DELETE", $"/api/feed/{postId}", Co)); // a coordinator is not the admin
        Assert.Equal(204, await Status("DELETE", $"/api/feed/{(string)linkOnly.Body["id"]!}", M2));
        Assert.Equal(204, await Status("DELETE", $"/api/feed/{postId}", Admin));
        Assert.Equal(404, await Status("DELETE", $"/api/feed/{postId}", Admin));
        Assert.Equal(0, await api.Count($"select count(*) from feed_likes where post_id = '{postId}'"));
    }

    [Fact]
    public async Task V10_feed_posts_and_comments_have_daily_limits_per_person()
    {
        for (var i = 0; i < 30; i++) Assert.Equal(201, await Status("POST", "/api/feed", Co, new { body = $"Post {i}" }));
        var limited = await api.Call("POST", "/api/feed", Co, new { body = "one too many" });
        Assert.Equal(429, limited.Status);
        Assert.Contains("30", (string)limited.Body["error"]!);
        Assert.Equal(201, await Status("POST", "/api/feed", M1, new { body = "others are not affected" }));

        var postId = (string)(await api.Call("GET", "/api/feed?limit=1", M1)).Body["items"]![0]!["id"]!;
        await api.Count($"with x as (insert into feed_comments (post_id, author_id, body) select '{postId}', '{M1.Id}', 'c' || g from generate_series(1, 199) g returning 1) select count(*) from x");
        Assert.Equal(201, await Status("POST", $"/api/feed/{postId}/comments", M1, new { body = "number 200" }));
        Assert.Equal(429, await Status("POST", $"/api/feed/{postId}/comments", M1, new { body = "number 201" }));
        Assert.Equal(201, await Status("POST", $"/api/feed/{postId}/comments", M2, new { body = "someone else" }));

        // Yesterday's posts do not count.
        await api.Count($"with x as (update feed_posts set created_at = now() - interval '2 days' where author_id = '{Co.Id}' returning 1) select count(*) from x");
        Assert.Equal(201, await Status("POST", "/api/feed", Co, new { body = "a new day" }));
        var page = (await api.Call("GET", "/api/feed?limit=50", M2)).Body["items"]!.AsArray();
        Assert.Equal(20, page.Single(x => (string)x!["id"]! == postId)!["comments"]!.AsArray().Count); // the last 20 only
        Assert.Equal(201, (int)page.Single(x => (string)x!["id"]! == postId)!["comment_count"]!);
    }
}
