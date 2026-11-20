using System.Text.Json.Nodes;

namespace FdeTracker.Tests;

/// <summary>Batches, the shared feed (sorting, recommended picks) and what each role may do.
/// Its own database (own fixture). Member M1 and M2 are learners, R is promoted to reviewer, Admin is the owner account.</summary>
[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class FeedBatchTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    // Sign-in is invite-only: these people are invited (as members of the seeded program) before they first sign in.
    public Task InitializeAsync() => api.Invite(M1, R, M2);
    public Task DisposeAsync() => Task.CompletedTask;

    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser M1 = new(ApiFixture.Id(201), "member.one@fiftyfivetech.io");
    static readonly TestUser R = new(ApiFixture.Id(202), "reviewer.r@fiftyfivetech.io");
    static readonly TestUser M2 = new(ApiFixture.Id(203), "member.two@fiftyfivetech.io");

    static string[] Titles(JsonNode? items) => items!.AsArray().Select(i => (string)i!["title"]!).ToArray();
    static object Item(string title, bool submit = true, string? url = null, string? summary = null) => url is null
        ? new { topic = "evaluation", stack_id = (string?)null, kind = "note", title, body_md = "text", summary, submit }
        : new { topic = "evaluation", stack_id = (string?)null, kind = "link", title, url, summary, submit };

    async Task<string> BatchId(string name) =>
        (string)(await api.Call("GET", "/api/admin/batches", Admin)).Body.AsArray().First(b => (string)b!["name"]! == name)!["id"]!;

    [Fact]
    public async Task F01_only_the_admin_creates_batches_and_reviewers_may_read_them()
    {
        foreach (var u in new[] { Admin, M1, R, M2 }) Assert.Equal(200, (await api.Call("GET", "/api/me", u)).Status);
        Assert.Equal(200, (await api.Call("POST", $"/api/admin/users/{R.Id}/role", Admin, new { role = "reviewer" })).Status);

        Assert.Equal(403, (await api.Call("GET", "/api/admin/batches", M1)).Status);
        Assert.Equal(403, (await api.Call("POST", "/api/admin/batches", M1, new { name = "Sneaky", starts_on = "2026-01-01" })).Status);
        Assert.Equal(200, (await api.Call("GET", "/api/admin/batches", R)).Status);
        Assert.Equal(403, (await api.Call("POST", "/api/admin/batches", R, new { name = "Sneaky", starts_on = "2026-01-01" })).Status);

        var a = await api.Call("POST", "/api/admin/batches", Admin, new { name = "Batch A", starts_on = "2026-01-05" });
        Assert.Equal(201, a.Status);
        Assert.Equal("Batch A", (string)a.Body["name"]!);
        Assert.Equal("2026-01-05", (string)a.Body["starts_on"]!);
        Assert.True(Guid.TryParse((string)a.Body["id"]!, out _));
        Assert.Equal(201, (await api.Call("POST", "/api/admin/batches", Admin, new { name = "  Batch B  ", starts_on = "2026-06-01" })).Status);
        Assert.Equal(409, (await api.Call("POST", "/api/admin/batches", Admin, new { name = "Batch A", starts_on = "2027-01-01" })).Status);

        foreach (var bad in new object[]
        {
            new { name = "Bad date", starts_on = "2026-02-30" },
            new { name = "Too early", starts_on = "2019-12-31" },
            new { name = "Too late", starts_on = "2101-01-01" },
            new { name = "Wrong shape", starts_on = "26-1-1" },
            new { name = "x", starts_on = "2026-01-01" },
            new { name = new string('n', 61), starts_on = "2026-01-01" },
            new { name = "No date" },
        })
            Assert.Equal(400, (await api.Call("POST", "/api/admin/batches", Admin, bad)).Status);

        var list = (await api.Call("GET", "/api/admin/batches", R)).Body.AsArray();
        Assert.Equal(new[] { "Batch B", "Batch A" }, list.Select(b => (string)b!["name"]!).ToArray()); // newest start first, name trimmed
        Assert.All(list, b => Assert.Equal(0, (int)b!["members"]!));
        Assert.True(await api.Count("select count(*) from audit_log where action = 'admin.batch_create'") >= 2);
    }

    [Fact]
    public async Task F02_the_admin_puts_people_in_a_batch_and_it_shows_on_me_and_the_user_list()
    {
        var a = await BatchId("Batch A");
        Assert.Null((await api.Call("GET", "/api/me", M1)).Body["batch"]);
        Assert.Equal(403, (await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", M1, new { batch_id = a })).Status);
        Assert.Equal(403, (await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", R, new { batch_id = a })).Status);

        var set = await api.Call("POST", $"/api/admin/users/{M1.Id}/batch", Admin, new { batch_id = a });
        Assert.Equal(200, set.Status);
        Assert.Equal(M1.Id.ToString(), (string)set.Body["id"]!);
        Assert.Equal(M1.Email, (string)set.Body["email"]!);
        Assert.Equal(a, (string)set.Body["batch_id"]!);

        Assert.Equal(404, (await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", Admin, new { batch_id = Guid.NewGuid() })).Status); // unknown batch
        Assert.Equal(404, (await api.Call("POST", $"/api/admin/users/{Guid.NewGuid()}/batch", Admin, new { batch_id = a })).Status); // unknown person
        Assert.Equal(400, (await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", Admin, new { })).Status);                      // batch_id must be sent
        Assert.Equal(400, (await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", Admin, new { batch_id = "nope" })).Status);

        var me = (await api.Call("GET", "/api/me", M1)).Body;
        Assert.Equal(a, (string)me["batch"]!["id"]!);
        Assert.Equal("Batch A", (string)me["batch"]!["name"]!);
        Assert.Equal("2026-01-05", (string)me["batch"]!["starts_on"]!);
        var other = (await api.Call("GET", "/api/me", M2)).Body.AsObject();
        Assert.True(other.ContainsKey("batch"));                              // always sent, null when not in a batch
        Assert.Null(other["batch"]);

        var users = (await api.Call("GET", "/api/admin/users", Admin)).Body.AsArray();
        var row = users.First(u => (string)u!["id"]! == M1.Id.ToString())!;
        Assert.Equal(a, (string)row["batch_id"]!);
        Assert.Equal("Batch A", (string)row["batch_name"]!);
        Assert.Null(users.First(u => (string)u!["id"]! == M2.Id.ToString())!["batch_name"]);
        Assert.Equal(1, (int)(await api.Call("GET", "/api/admin/batches", Admin)).Body.AsArray().First(b => (string)b!["name"]! == "Batch A")!["members"]!);

        // null takes a person out; put them back for the rest of the scenario
        await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", Admin, new { batch_id = a });
        var removed = await api.Call("POST", $"/api/admin/users/{M2.Id}/batch", Admin, new { batch_id = (Guid?)null });
        Assert.Equal(200, removed.Status);
        Assert.Null(removed.Body["batch_id"]);
        Assert.Null((await api.Call("GET", "/api/me", M2)).Body["batch"]);
        Assert.True(await api.Count("select count(*) from audit_log where action = 'admin.batch'") >= 3);
    }

    [Fact]
    public async Task F03_the_admin_sets_member_or_reviewer_for_anyone_but_the_owner_account()
    {
        Assert.Equal("reviewer", (string)(await api.Call("POST", $"/api/admin/users/{M2.Id}/role", Admin, new { role = "reviewer" })).Body["role"]!);
        Assert.Equal("member", (string)(await api.Call("POST", $"/api/admin/users/{M2.Id}/role", Admin, new { role = "member" })).Body["role"]!);
        Assert.Equal(404, (await api.Call("POST", $"/api/admin/users/{Admin.Id}/role", Admin, new { role = "member" })).Status); // the owner stays admin
        Assert.Equal(400, (await api.Call("POST", $"/api/admin/users/{M2.Id}/role", Admin, new { role = "admin" })).Status);    // nobody else becomes admin
        Assert.Equal(403, (await api.Call("POST", $"/api/admin/users/{M2.Id}/role", R, new { role = "reviewer" })).Status);    // a reviewer cannot hand out roles
        Assert.Equal("admin", (string)(await api.Call("GET", "/api/me", Admin)).Body["role"]!);
        Assert.Equal("member", (string)(await api.Call("GET", "/api/me", M2)).Body["role"]!);
    }

    [Fact]
    public async Task F04_the_review_team_publishes_straight_away_while_a_member_waits_for_review()
    {
        var byReviewer = await api.Call("POST", "/api/content", R, Item("Zeta reviewer pick"));
        Assert.Equal(201, byReviewer.Status);
        Assert.Equal("approved", (string)byReviewer.Body["status"]!);
        var byAdmin = await api.Call("POST", "/api/content", Admin, Item("alpha admin note"));
        Assert.Equal("approved", (string)byAdmin.Body["status"]!);
        var byMember = await api.Call("POST", "/api/content", M1, Item("Member link for review", url: "https://example.com/m1", summary: "Why this helps"));
        Assert.Equal("in_review", (string)byMember.Body["status"]!);
        Assert.Equal("draft", (string)(await api.Call("POST", "/api/content", R, Item("Reviewer draft", submit: false))).Body["status"]!); // a draft stays a draft

        // A reviewer's draft that is submitted later is also published at once.
        var draft = (string)(await api.Call("GET", "/api/content/mine", R)).Body.AsArray().First(x => (string)x!["title"]! == "Reviewer draft")!["id"]!;
        var sent = await api.Call("PATCH", $"/api/content/{draft}", R, new { title = "Middle reviewer note", submit = true });
        Assert.Equal("approved", (string)sent.Body["status"]!);

        var feed = Titles((await api.Call("GET", "/api/content/feed?topic=evaluation", M2)).Body["items"]);
        Assert.Contains("Zeta reviewer pick", feed);
        Assert.Contains("alpha admin note", feed);
        Assert.Contains("Middle reviewer note", feed);
        Assert.DoesNotContain("Member link for review", feed);
        Assert.NotNull((await api.Call("GET", $"/api/content/{(string)byReviewer.Body["id"]!}", M2)).Body["approved_at"]);
        Assert.Equal(3, await api.Count("select count(*) from audit_log where action = 'content.auto_approved'"));
        Assert.Equal(0, await api.Count("select count(*) from content_items where status = 'approved' and (approved_at is null or submitted_at is null)"));
    }

    [Fact]
    public async Task F05_the_feed_sorts_newest_oldest_by_title_and_pages_only_newest_first()
    {
        // In creation order: Zeta reviewer pick, alpha admin note, Middle reviewer note (published by the PATCH, last).
        async Task<string[]> Sorted(string sort, string extra = "") => Titles((await api.Call("GET", $"/api/content/feed?topic=evaluation&sort={sort}{extra}", M2)).Body["items"]);
        Assert.Equal(new[] { "Middle reviewer note", "alpha admin note", "Zeta reviewer pick" }, await Sorted("newest"));
        Assert.Equal(new[] { "Middle reviewer note", "alpha admin note", "Zeta reviewer pick" }, Titles((await api.Call("GET", "/api/content/feed?topic=evaluation", M2)).Body["items"])); // default
        Assert.Equal(new[] { "Zeta reviewer pick", "alpha admin note", "Middle reviewer note" }, await Sorted("oldest"));
        Assert.Equal(new[] { "alpha admin note", "Middle reviewer note", "Zeta reviewer pick" }, await Sorted("title")); // ignores capital letters

        var newestPage = await api.Call("GET", "/api/content/feed?topic=evaluation&sort=newest&limit=1", M2);
        Assert.NotNull(newestPage.Body["next"]);
        var second = await api.Call("GET", $"/api/content/feed?topic=evaluation&sort=newest&limit=1&before={Uri.EscapeDataString((string)newestPage.Body["next"]!)}", M2);
        Assert.Equal(new[] { "alpha admin note" }, Titles(second.Body["items"]));
        foreach (var sort in new[] { "oldest", "title", "recommended" })
        {
            var page = await api.Call("GET", $"/api/content/feed?topic=evaluation&sort={sort}&limit=1", M2);
            Assert.Single(page.Body["items"]!.AsArray());
            Assert.Null(page.Body["next"]); // one page only for these orders
        }

        Assert.Equal(200, (await api.Call("GET", "/api/content/feed?limit=50", M2)).Status);
        Assert.Equal(400, (await api.Call("GET", "/api/content/feed?limit=51", M2)).Status);
        Assert.Equal(400, (await api.Call("GET", "/api/content/feed?sort=popular", M2)).Status);
        Assert.Equal(400, (await api.Call("GET", "/api/content/feed?sort=newest;drop", M2)).Status);
    }

    [Fact]
    public async Task F06_only_the_review_team_recommends_and_only_approved_items()
    {
        var items = (await api.Call("GET", "/api/content/feed?topic=evaluation&sort=oldest", M2)).Body["items"]!.AsArray();
        string zeta = (string)items[0]!["id"]!, alpha = (string)items[1]!["id"]!;
        Assert.All(items, i => Assert.False((bool)i!["recommended"]!));
        Assert.All(items, i => Assert.False(i!.AsObject().ContainsKey("author_role"))); // roles are never shown

        Assert.Equal(403, (await api.Call("POST", $"/api/content/{zeta}/recommend", M1, new { recommended = true })).Status);
        var pick = await api.Call("POST", $"/api/content/{zeta}/recommend", R, new { recommended = true });
        Assert.Equal(200, pick.Status);
        Assert.True((bool)pick.Body["recommended"]!);
        Assert.Equal("Zeta reviewer pick", (string)pick.Body["title"]!);
        Assert.Equal("evaluation", (string)pick.Body["topic"]!);
        Assert.Equal(200, (await api.Call("POST", $"/api/content/{alpha}/recommend", Admin, new { recommended = true })).Status);

        var waiting = (string)(await api.Call("GET", "/api/content/mine", M1)).Body.AsArray().First(x => (string)x!["title"]! == "Member link for review")!["id"]!;
        Assert.Equal(409, (await api.Call("POST", $"/api/content/{waiting}/recommend", R, new { recommended = true })).Status); // not approved
        Assert.Equal(404, (await api.Call("POST", $"/api/content/{Guid.NewGuid()}/recommend", R, new { recommended = true })).Status);
        Assert.Equal(400, (await api.Call("POST", $"/api/content/{zeta}/recommend", R, new { })).Status);
        Assert.Equal(400, (await api.Call("POST", $"/api/content/{zeta}/recommend", R, new { recommended = "yes" })).Status);

        // Recommended first (newest of those first), then the rest newest first.
        Assert.Equal(new[] { "alpha admin note", "Zeta reviewer pick", "Middle reviewer note" },
            Titles((await api.Call("GET", "/api/content/feed?topic=evaluation&sort=recommended", M2)).Body["items"]));
        Assert.True((bool)(await api.Call("GET", $"/api/content/{zeta}", M2)).Body["recommended"]!);

        var off = await api.Call("POST", $"/api/content/{alpha}/recommend", R, new { recommended = false });
        Assert.False((bool)off.Body["recommended"]!);
        Assert.Equal(0, await api.Count($"select count(*) from content_items where id = '{alpha}' and recommended_at is not null"));
        Assert.Equal(new[] { "Zeta reviewer pick", "Middle reviewer note", "alpha admin note" },
            Titles((await api.Call("GET", "/api/content/feed?topic=evaluation&sort=recommended", M2)).Body["items"]));
        Assert.Equal(3, await api.Count("select count(*) from audit_log where action in ('content.recommend', 'content.unrecommend')"));
    }

    [Fact]
    public async Task F07_the_review_queue_carries_what_a_reviewer_needs_to_decide()
    {
        Assert.Equal(403, (await api.Call("GET", "/api/review/queue", M2)).Status);
        var queue = await api.Call("GET", "/api/review/queue", R);
        Assert.Equal(200, queue.Status);
        var row = queue.Body.AsArray().Single(x => (string)x!["title"]! == "Member link for review")!;
        Assert.Equal("link", (string)row["kind"]!);
        Assert.Equal("https://example.com/m1", (string)row["url"]!);
        Assert.Equal("Why this helps", (string)row["summary"]!);
        Assert.Equal("evaluation", (string)row["topic"]!);
        Assert.Equal("AI evaluation", (string)row["topic_title"]!);
        Assert.Equal("member.one", (string)row["author"]!);
        Assert.NotNull(row["submitted_at"]);
        Assert.True(Guid.TryParse((string)row["id"]!, out _));
        Assert.False(row.AsObject().ContainsKey("author_id"));
    }
}
