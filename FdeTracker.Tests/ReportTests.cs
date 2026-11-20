using System.Text.Json.Nodes;

namespace FdeTracker.Tests;

/// <summary>Progress reports per person: a learner's own report, and the reviewers' list of everyone and anyone's report.
/// Its own database (own fixture). M1 and M2 are learners, R is promoted to reviewer, P stays pending (not invited), Admin is the owner account.</summary>
[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class ReportTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    // Sign-in is invite-only: these people are invited (as members of the seeded program) before they first sign in.
    public Task InitializeAsync() => api.Invite(M1, M2, R);
    public Task DisposeAsync() => Task.CompletedTask;

    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser M1 = new(ApiFixture.Id(301), "member.one@fiftyfivetech.io");
    static readonly TestUser M2 = new(ApiFixture.Id(302), "member.two@fiftyfivetech.io");
    static readonly TestUser R = new(ApiFixture.Id(303), "reviewer.r@fiftyfivetech.io");
    static readonly TestUser P = new(ApiFixture.Id(304), "pending.p@fiftyfivetech.io");

    static string Key(string day) => $"/api/submissions/{day}";

    async Task Hand(string day, int rating, bool submit)
    {
        Assert.Equal(200, (await api.Call("PUT", Key(day), M1, new { note = "What I did on " + day, learned = "Learned on " + day, rating })).Status);
        Assert.Equal(200, (await api.Call("POST", Key(day) + "/links", M1, new { label = "Proof", url = "https://example.com/" + day })).Status);
        if (submit) Assert.Equal(200, (await api.Call("POST", Key(day) + "/submit", M1)).Status);
    }

    async Task<string> SubmissionId(string day) =>
        (string)(await api.Call("GET", "/api/review/submissions?status=all", R)).Body.AsArray().First(x => (string)x!["task_key"]! == day)!["id"]!;

    static JsonNode Day(JsonNode report, string key) => report["days"]!.AsArray().First(d => (string)d!["task_key"]! == key)!;

    // No name anywhere in a report carries an email address (the test texts themselves contain no "@").
    static void NoEmails(JsonNode node) => Assert.DoesNotContain("@", node.ToJsonString());

    [Fact]
    public async Task R01_a_member_reads_their_own_report_with_counts_average_and_the_last_review()
    {
        foreach (var u in new[] { Admin, M1, M2, R }) Assert.Equal(200, (await api.Call("GET", "/api/me", u)).Status);
        Assert.Equal("pending", (string)(await api.Call("GET", "/api/me", P)).Body["access"]!);
        Assert.Equal(200, (await api.Call("POST", $"/api/admin/users/{R.Id}/role", Admin, new { role = "reviewer" })).Status);
        Assert.Equal(200, (await api.Call("PATCH", "/api/me", M1, new { display_name = "Asha" })).Status);
        var batch = await api.Call("POST", "/api/admin/batches", Admin, new { name = "Batch A", starts_on = "2026-01-05" });
        Assert.Equal(201, batch.Status);
        Assert.Equal(200, (await api.Call("POST", $"/api/admin/users/{M1.Id}/batch", Admin, new { batch_id = (string)batch.Body["id"]! })).Status);

        var empty = (await api.Call("GET", "/api/reports/me", M2)).Body;
        Assert.Empty(empty["days"]!.AsArray());
        Assert.Equal(0, (int)empty["totals"]!["submitted"]!);
        Assert.Null(empty["totals"]!["avg_rating"]);
        Assert.Null(empty["person"]!["batch"]);
        Assert.Equal("member.two", (string)empty["person"]!["name"]!);

        await Hand("week1-day10", 5, submit: true);
        await Hand("week1-day2", 4, submit: true);
        await Hand("week1-day1", 2, submit: false); // saved, not submitted
        Assert.Equal(200, (await api.Call("POST", $"/api/review/submissions/{await SubmissionId("week1-day2")}", R, new { decision = "request_changes", comment = "Add the token counts." })).Status);
        Assert.Equal(200, (await api.Call("POST", $"/api/review/submissions/{await SubmissionId("week1-day10")}", R, new { decision = "approve", comment = "Good." })).Status);

        var me = await api.Call("GET", "/api/reports/me", M1);
        Assert.Equal(200, me.Status);
        var rep = me.Body;
        Assert.Equal(M1.Id.ToString(), (string)rep["person"]!["id"]!);
        Assert.Equal("Asha", (string)rep["person"]!["name"]!);
        Assert.Equal("Batch A", (string)rep["person"]!["batch"]!["name"]!);
        Assert.Equal("2026-01-05", (string)rep["person"]!["batch"]!["starts_on"]!);
        Assert.Equal(new[] { "week1-day1", "week1-day2", "week1-day10" }, rep["days"]!.AsArray().Select(d => (string)d!["task_key"]!).ToArray()); // natural order

        var t = rep["totals"]!;
        Assert.Equal(0, (int)t["submitted"]!);
        Assert.Equal(1, (int)t["approved"]!);
        Assert.Equal(1, (int)t["changes_requested"]!);
        Assert.Equal(1, (int)t["drafts"]!);
        Assert.Equal(4.5, (double)t["avg_rating"]!); // (5 + 4) / 2: the draft's 2 is private and not counted

        var d2 = Day(rep, "week1-day2");
        Assert.Equal("changes_requested", (string)d2["status"]!);
        Assert.Equal("request_changes", (string)d2["last_decision"]!);
        Assert.Equal("Add the token counts.", (string)d2["last_comment"]!);
        Assert.Equal("reviewer.r", (string)d2["last_reviewer"]!);
        Assert.Equal(4, (int)d2["rating"]!);
        Assert.Equal(1, (int)d2["item_count"]!);
        Assert.NotNull(d2["submitted_at"]);
        Assert.NotNull(d2["reviewed_at"]);
        Assert.NotNull(d2["updated_at"]);
        Assert.Equal("approve", (string)Day(rep, "week1-day10")["last_decision"]!);

        var d1 = Day(rep, "week1-day1");
        Assert.Equal("draft", (string)d1["status"]!);
        Assert.Equal("Learned on week1-day1", (string)d1["learned"]!); // the learner sees their own saved work
        Assert.Equal(2, (int)d1["rating"]!);
        Assert.Null(d1["submitted_at"]);
        Assert.True(d1.AsObject().ContainsKey("last_decision"));
        Assert.Null(d1["last_decision"]);
        NoEmails(rep);
        Assert.Equal(0, await api.Count("select count(*) from audit_log where action like 'report%'")); // read-only, nothing logged
    }

    [Fact]
    public async Task R02_members_cannot_read_other_people_and_signed_out_or_pending_people_are_refused()
    {
        Assert.Equal(403, (await api.Call("GET", "/api/reports/people", M1)).Status);
        Assert.Equal(403, (await api.Call("GET", $"/api/reports/people/{M2.Id}", M1)).Status);
        Assert.Equal(403, (await api.Call("GET", $"/api/reports/people/{M1.Id}", M1)).Status); // even their own: they use /me
        Assert.Equal(401, (await api.Call("GET", "/api/reports/me")).Status);
        Assert.Equal(401, (await api.Call("GET", "/api/reports/people")).Status);
        Assert.Equal(401, (await api.Call("GET", $"/api/reports/people/{M1.Id}")).Status);
        Assert.Equal(403, (await api.Call("GET", "/api/reports/me", P)).Status); // waiting for approval
    }

    [Fact]
    public async Task R03_reviewers_see_everyone_approved_sorted_by_name_with_counts_and_a_batch_filter()
    {
        var res = await api.Call("GET", "/api/reports/people", R);
        Assert.Equal(200, res.Status);
        var rows = res.Body.AsArray();
        Assert.Equal(new[] { "Asha", "boss", "member.two", "reviewer.r" }, rows.Select(r => (string)r!["name"]!).ToArray()); // pending P is not listed
        Assert.All(rows, r => Assert.DoesNotContain("@", (string)r!["name"]!));
        NoEmails(res.Body);

        var m1 = rows.First(r => (string)r!["id"]! == M1.Id.ToString())!;
        Assert.Equal("Batch A", (string)m1["batch_name"]!);
        Assert.Equal("2026-01-05", (string)m1["batch_starts_on"]!);
        Assert.Equal(0, (int)m1["submitted"]!);
        Assert.Equal(1, (int)m1["approved"]!);
        Assert.Equal(1, (int)m1["changes_requested"]!);
        Assert.Equal(1, (int)m1["drafts"]!);
        Assert.Equal(4.5, (double)m1["avg_rating"]!);
        Assert.NotNull(m1["last_activity"]);

        var m2 = rows.First(r => (string)r!["id"]! == M2.Id.ToString())!.AsObject();
        Assert.Equal(0, (int)m2["approved"]!);
        Assert.True(m2.ContainsKey("avg_rating"));
        Assert.Null(m2["avg_rating"]);
        Assert.Null(m2["last_activity"]);
        Assert.Null(m2["batch_name"]);

        var batchId = (string)(await api.Call("GET", "/api/admin/batches", R)).Body.AsArray().First(b => (string)b!["name"]! == "Batch A")!["id"]!;
        var inBatch = (await api.Call("GET", $"/api/reports/people?batch={batchId}", Admin)).Body.AsArray();
        Assert.Equal(new[] { M1.Id.ToString() }, inBatch.Select(r => (string)r!["id"]!).ToArray());
        Assert.Empty((await api.Call("GET", $"/api/reports/people?batch={Guid.NewGuid()}", R)).Body.AsArray());
        Assert.Equal(400, (await api.Call("GET", "/api/reports/people?batch=not-a-uuid", R)).Status);
    }

    [Fact]
    public async Task R04_reviewers_and_the_admin_read_anyones_report_but_not_the_inside_of_drafts()
    {
        foreach (var viewer in new[] { R, Admin })
        {
            var res = await api.Call("GET", $"/api/reports/people/{M1.Id}", viewer);
            Assert.Equal(200, res.Status);
            var rep = res.Body;
            Assert.Equal("Asha", (string)rep["person"]!["name"]!);
            Assert.Equal(3, rep["days"]!.AsArray().Count);
            Assert.Equal(1, (int)rep["totals"]!["drafts"]!);
            Assert.Equal(4.5, (double)rep["totals"]!["avg_rating"]!);
            Assert.Equal("Add the token counts.", (string)Day(rep, "week1-day2")["last_comment"]!);
            Assert.Equal("Learned on week1-day2", (string)Day(rep, "week1-day2")["learned"]!);

            var draft = Day(rep, "week1-day1");
            Assert.Equal("draft", (string)draft["status"]!);  // they see that a draft exists...
            Assert.Null(draft["learned"]);                     // ...but drafts stay private to their owner
            Assert.Null(draft["note"]);
            Assert.Null(draft["rating"]);
            NoEmails(rep);
        }

        var own = await api.Call("GET", $"/api/reports/people/{R.Id}", R); // a reviewer may take part too; an empty report
        Assert.Equal(200, own.Status);
        Assert.Equal("reviewer.r", (string)own.Body["person"]!["name"]!);
        Assert.Empty(own.Body["days"]!.AsArray());

        Assert.Equal(404, (await api.Call("GET", $"/api/reports/people/{Guid.NewGuid()}", R)).Status);
        Assert.Equal(404, (await api.Call("GET", $"/api/reports/people/{P.Id}", Admin)).Status); // not approved: no report
        Assert.Equal(404, (await api.Call("GET", "/api/reports/people/not-a-uuid", R)).Status);
    }
}
