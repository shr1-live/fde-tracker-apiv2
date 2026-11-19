using System.Text;
using System.Text.Json.Nodes;
using Npgsql;

namespace FdeTracker.Tests;

/// <summary>The feed at scale (docs/PROGRAMS-V3.md, Feed): sorts with keyset paging, filters, search, one post, edits and the stored counters.
/// Its own database. Co coordinates the FDE program, A and B are members.</summary>
[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class FeedScaleTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser Co = new(ApiFixture.Id(501), "feed.coord@fiftyfivetech.io");
    static readonly TestUser A = new(ApiFixture.Id(502), "feed.a@fiftyfivetech.io");
    static readonly TestUser B = new(ApiFixture.Id(503), "feed.b@fiftyfivetech.io");
    static readonly TestUser Nobody = new(ApiFixture.Id(504), "feed.nobody@fiftyfivetech.io");

    // Shared between the ordered steps: post id -> (author, kind).
    static readonly Dictionary<string, (TestUser Author, string Kind)> Posts = [];
    static string Percent = "", Plain = "", Underscore = "", NotUnderscore = "";

    async Task<int> Status(string method, string path, TestUser user, object? body = null) => (await api.Call(method, path, user, body)).Status;

    async Task<List<string>> Ids(string sql)
    {
        await using var conn = new NpgsqlConnection(api.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetGuid(0).ToString());
        return list;
    }

    /// <summary>Walks every page of a feed query and returns the post ids in order, checking each page's size and that it ends.</summary>
    async Task<List<JsonNode>> All(string query, TestUser who, int limit = 7)
    {
        var items = new List<JsonNode>();
        string? cursor = null;
        for (var pages = 0; pages < 50; pages++)
        {
            var sep = query.Length == 0 ? "" : "&";
            var res = await api.Call("GET", $"/api/feed?limit={limit}{sep}{query}{(cursor is null ? "" : $"&cursor={cursor}")}", who);
            Assert.Equal(200, res.Status);
            var page = res.Body["items"]!.AsArray();
            items.AddRange(page.Select(x => x!));
            cursor = (string?)res.Body["next_cursor"];
            if (cursor is null) { Assert.True(page.Count <= limit); return items; }
            Assert.Equal(limit, page.Count);
        }
        throw new InvalidOperationException("Paging never ended");
    }

    static List<string> IdsOf(List<JsonNode> items) => items.Select(x => (string)x["id"]!).ToList();

    [Fact]
    public async Task F01_twenty_five_posts_with_likes_and_comments()
    {
        var me = (await api.Call("GET", "/api/me", Admin)).Body;
        var fde = (string)me["programs"]!.AsArray().Single(p => (string)p!["name"]! == "FDE Training Program")!["id"]!;
        Assert.Equal("invited", (string)(await api.Call("POST", $"/api/programs/{fde}/invites", Admin, new { email = Co.Email, role = "coordinator" })).Body["status"]!);
        await api.Invite(A, B);
        foreach (var u in new[] { Co, A, B, Nobody }) Assert.Equal(200, await Status("GET", "/api/me", u));
        Assert.Equal(0, await api.Count("select count(*) from feed_posts"));

        var authors = new[] { A, B, Co, Admin };
        var urls = new (string? Url, string Kind)[]
        {
            (null, "text"),
            ("https://www.youtube.com/watch?v=abc", "video"),
            ("https://youtu.be/xyz", "video"),
            ("https://example.com/read", "link"),
            ("https://youtube.com.evil.example/watch", "link"),
            ("https://notyoutube.com/x", "link"),
            ("https://m.YouTube.com/shorts/1", "video"),
        };
        for (var i = 0; i < 25; i++)
        {
            var (url, kind) = urls[i % urls.Length];
            var body = i switch
            {
                0 => "Day 1: 100% done",
                1 => "Day 1: 100 done",
                2 => "renamed to snake_case today",
                3 => "renamed to snakeXcase today",
                _ => url is null || i % 2 == 0 ? $"Post number {i}" : null,
            };
            var res = await api.Call("POST", "/api/feed", authors[i % 4], new { body, url });
            Assert.Equal(201, res.Status);
            var id = (string)res.Body["id"]!;
            Assert.Null(res.Body["edited_at"]);
            Posts[id] = (authors[i % 4], kind);
            if (i == 0) Percent = id;
            if (i == 1) Plain = id;
            if (i == 2) Underscore = id;
            if (i == 3) NotUnderscore = id;
        }
        // Ties on purpose: five posts share each time, so the id breaks the tie.
        await api.Count("with x as (update feed_posts set created_at = timestamptz '2026-11-01 10:00:00+00' + ((abs(hashtext(id::text)) % 5) * interval '1 minute') returning 1) select count(*) from x");

        var ids = Posts.Keys.ToList();
        var likers = new[] { A, B, Co, Admin };
        for (var i = 0; i < ids.Count; i++)
        {
            for (var l = 0; l < i % 5; l++) Assert.Equal(200, await Status("POST", $"/api/feed/{ids[i]}/like", likers[l % 4]));
            for (var c = 0; c < i % 4; c++) Assert.Equal(201, await Status("POST", $"/api/feed/{ids[i]}/comments", likers[(i + c) % 4], new { body = $"comment {c}" }));
        }
        await CountersMatchRows();
    }

    async Task CountersMatchRows()
    {
        Assert.Equal(0, await api.Count(
            """
            select count(*) from feed_posts p
            where p.like_count <> (select count(*) from feed_likes l where l.post_id = p.id)
               or p.comment_count <> (select count(*) from feed_comments c where c.post_id = p.id)
            """));
        var all = await All("", A, 50);
        foreach (var p in all)
        {
            var id = (string)p["id"]!;
            Assert.Equal(await api.Count($"select count(*) from feed_likes where post_id = '{id}'"), (int)p["like_count"]!);
            Assert.Equal(await api.Count($"select count(*) from feed_comments where post_id = '{id}'"), (int)p["comment_count"]!);
        }
    }

    [Fact]
    public async Task F02_every_sort_pages_through_everything_in_order_with_no_gaps_or_repeats()
    {
        var orders = new Dictionary<string, string>
        {
            ["new"] = "created_at desc, id desc",
            ["top"] = "(select count(*) from feed_likes l where l.post_id = p.id) desc, created_at desc, id desc",
            ["discussed"] = "(select count(*) from feed_comments c where c.post_id = p.id) desc, created_at desc, id desc",
        };
        foreach (var (sort, order) in orders)
        {
            var expected = await Ids($"select id from feed_posts p order by {order}");
            Assert.Equal(25, expected.Count);
            foreach (var limit in new[] { 1, 7, 20, 25, 50 })
            {
                var got = IdsOf(await All($"sort={sort}", B, limit));
                Assert.Equal(expected, got);
            }
        }
        // The default sort is new, and the default page is 20.
        var first = (await api.Call("GET", "/api/feed", B)).Body;
        Assert.Equal(20, first["items"]!.AsArray().Count);
        Assert.NotNull((string?)first["next_cursor"]);
        Assert.Equal(await Ids("select id from feed_posts order by created_at desc, id desc limit 20"), IdsOf(first["items"]!.AsArray().Select(x => x!).ToList()));
        // liked_by_me is per person.
        var top = (await api.Call("GET", "/api/feed?sort=top&limit=1", B)).Body["items"]![0]!;
        Assert.True((bool)top["liked_by_me"]!); // the most liked post has 4 likes: everyone, B included
    }

    [Fact]
    public async Task F03_kind_mine_and_search_filters()
    {
        foreach (var kind in new[] { "video", "link", "text" })
        {
            var got = IdsOf(await All($"kind={kind}", A, 4));
            Assert.Equal(Posts.Where(p => p.Value.Kind == kind).Select(p => p.Key).OrderBy(x => x), got.OrderBy(x => x));
            Assert.Equal(got.Count, got.Distinct().Count());
        }
        Assert.Equal(25, (await All("kind=all", A, 50)).Count);

        var mine = IdsOf(await All("mine=true", A, 3));
        Assert.Equal(Posts.Where(p => p.Value.Author == A).Select(p => p.Key).OrderBy(x => x), mine.OrderBy(x => x));
        Assert.Equal(25, (await All("mine=false", A, 50)).Count);
        var mineTop = IdsOf(await All("mine=true&sort=top&kind=text", B, 2));
        Assert.Equal(Posts.Where(p => p.Value.Author == B && p.Value.Kind == "text").Select(p => p.Key).OrderBy(x => x), mineTop.OrderBy(x => x));

        // q is a literal, case-insensitive search in the text and the link.
        Assert.Equal(new[] { Percent }, IdsOf(await All($"q={Uri.EscapeDataString("100%")}", A)));
        Assert.Equal(new[] { Percent, Plain }.Order(), IdsOf(await All("q=100", A, 50)).Order());
        Assert.Equal(new[] { Underscore }, IdsOf(await All("q=SNAKE_case", A)));
        Assert.Equal(new[] { Underscore, NotUnderscore }.Order(), IdsOf(await All("q=snake", A)).Order());
        Assert.Equal(new[] { Percent }, IdsOf(await All($"q={Uri.EscapeDataString("%")}", A))); // % is not a wildcard
        Assert.Equal(new[] { Underscore }, IdsOf(await All($"q={Uri.EscapeDataString("_")}", A))); // nor is _
        Assert.Empty(await All($"q={Uri.EscapeDataString("!")}", A)); // nor the escape character
        Assert.Equal(4, (await All("q=youtu.be", A, 3)).Count); // the link is searched too: posts 2, 9, 16 and 23
        var urlHits = await All("q=EXAMPLE.com&kind=link", A, 2);
        Assert.All(urlHits, x => Assert.Contains("example.com", (string)x["url"]!));
        Assert.Equal(4, urlHits.Count); // posts 3, 10, 17 and 24
        Assert.Equal(25, (await All("q=%20%20", A, 50)).Count); // blank search = no search
        Assert.Equal(200, await Status("GET", $"/api/feed?q={new string('a', 100)}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?q={new string('a', 101)}", A));
    }

    [Fact]
    public async Task F04_bad_query_values_are_400()
    {
        Assert.Equal(400, await Status("GET", "/api/feed?sort=oldest", A));
        Assert.Equal(400, await Status("GET", "/api/feed?kind=image", A));
        Assert.Equal(400, await Status("GET", "/api/feed?mine=maybe", A));
        Assert.Equal(400, await Status("GET", "/api/feed?limit=0", A));
        Assert.Equal(400, await Status("GET", "/api/feed?limit=51", A));
        Assert.Equal(400, await Status("GET", "/api/feed?cursor=garbage", A));
        Assert.Equal(400, await Status("GET", "/api/feed?cursor=%25%25%25", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?cursor={new string('A', 300)}", A));

        var newCursor = (string)(await api.Call("GET", "/api/feed?limit=2", A)).Body["next_cursor"]!;
        Assert.Equal(200, await Status("GET", $"/api/feed?cursor={newCursor}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=top&cursor={newCursor}", A)); // a cursor belongs to its sort
        var topCursor = (string)(await api.Call("GET", "/api/feed?sort=top&limit=2", A)).Body["next_cursor"]!;
        Assert.Equal(200, await Status("GET", $"/api/feed?sort=top&cursor={topCursor}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=discussed&cursor={topCursor}", A));

        static string B64(string raw) => Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var id = Guid.NewGuid().ToString("N");
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=top&cursor={B64($"top|x|1|{id}")}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=top&cursor={B64($"top|-1|1|{id}")}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=top&cursor={B64($"top|1|99999999999999999999|{id}")}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?sort=top&cursor={B64("top|1|1|not-a-guid")}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?cursor={B64($"new|1|1|{id}|extra")}", A));
        Assert.Equal(400, await Status("GET", $"/api/feed?cursor={B64($"new|5|1|{id}")}", A));
        Assert.Equal(200, await Status("GET", $"/api/feed?cursor={B64($"new|0|{DateTime.UtcNow.Ticks}|{id}")}", A)); // well formed is fine
        Assert.Equal(403, await Status("GET", "/api/feed", Nobody));
    }

    [Fact]
    public async Task F05_one_post_with_its_last_fifty_comments()
    {
        var res = await api.Call("GET", $"/api/feed/{Percent}", B);
        Assert.Equal(200, res.Status);
        Assert.Equal(Percent, (string)res.Body["id"]!);
        Assert.Equal("Day 1: 100% done", (string)res.Body["body"]!);
        Assert.Null(res.Body["edited_at"]);
        Assert.Equal(404, await Status("GET", $"/api/feed/{Guid.NewGuid()}", B));
        Assert.Equal(404, await Status("GET", "/api/feed/not-a-guid", B));
        Assert.Equal(403, await Status("GET", $"/api/feed/{Percent}", Nobody));

        await api.Count($"with x as (insert into feed_comments (post_id, author_id, body, created_at) select '{Plain}', '{A.Id}', 'bulk ' || g, now() + g * interval '1 second' from generate_series(1, 60) g returning 1) select count(*) from x");
        var one = (await api.Call("GET", $"/api/feed/{Plain}", A)).Body;
        var comments = one["comments"]!.AsArray();
        Assert.Equal(50, comments.Count);
        Assert.Equal("bulk 11", (string)comments[0]!["body"]!); // oldest first
        Assert.Equal("bulk 60", (string)comments[49]!["body"]!);
        Assert.Equal(61, (int)one["comment_count"]!); // 1 from F01 + 60, counted by the trigger
        var inList = (await All("q=100", A)).Single(x => (string)x["id"]! == Plain);
        Assert.Equal(20, inList["comments"]!.AsArray().Count); // the list keeps the last 20
    }

    [Fact]
    public async Task F06_only_the_author_can_edit_with_the_same_rules_as_a_new_post()
    {
        var (id, author) = Posts.First(p => p.Value.Author == A && p.Value.Kind == "text") switch { var p => (p.Key, p.Value.Author) };
        var before = (await api.Call("GET", $"/api/feed/{id}", A)).Body;

        Assert.Equal(403, await Status("PATCH", $"/api/feed/{id}", B, new { body = "hijack" }));
        Assert.Equal(403, await Status("PATCH", $"/api/feed/{id}", Co, new { body = "hijack" }));
        Assert.Equal(403, await Status("PATCH", $"/api/feed/{id}", Admin, new { body = "hijack" })); // the admin may delete, not rewrite
        Assert.Equal(403, await Status("PATCH", $"/api/feed/{id}", Nobody, new { body = "hijack" }));
        Assert.Equal(404, await Status("PATCH", $"/api/feed/{Guid.NewGuid()}", A, new { body = "x" }));

        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { }));
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { body = (string?)null })); // a text post cannot lose its text without a link
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { body = "   " }));
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { body = new string('a', 2001) }));
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { url = "http://example.com" }));
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { url = "https://example.com/" + new string('a', 490) }));
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { body = "x", extra = 1 }));
        Assert.Equal((string?)before["body"], (string?)(await api.Call("GET", $"/api/feed/{id}", A)).Body["body"]);
        Assert.Null((await api.Call("GET", $"/api/feed/{id}", A)).Body["edited_at"]);

        var edited = await api.Call("PATCH", $"/api/feed/{id}", author, new { body = "  Better words  " });
        Assert.Equal(200, edited.Status);
        Assert.Equal("Better words", (string)edited.Body["body"]!);
        Assert.Null(edited.Body["url"]);
        Assert.NotNull((string?)edited.Body["edited_at"]);
        Assert.Equal((string)before["created_at"]!, (string)edited.Body["created_at"]!);
        Assert.Equal((int)before["like_count"]!, (int)edited.Body["like_count"]!);
        Assert.Equal((int)before["comment_count"]!, (int)edited.Body["comment_count"]!);

        // Adding a link turns a text post into a link post; then the text can go.
        edited = await api.Call("PATCH", $"/api/feed/{id}", author, new { url = "https://youtu.be/new" });
        Assert.Equal(200, edited.Status);
        Assert.Equal("Better words", (string)edited.Body["body"]!);
        Assert.Contains(id, IdsOf(await All("kind=video", A, 50)));
        Assert.DoesNotContain(id, IdsOf(await All("kind=text", A, 50)));
        edited = await api.Call("PATCH", $"/api/feed/{id}", author, new { body = "" });
        Assert.Equal(200, edited.Status);
        Assert.Null(edited.Body["body"]);
        Assert.Equal("https://youtu.be/new", (string)edited.Body["url"]!);
        Assert.Equal(400, await Status("PATCH", $"/api/feed/{id}", author, new { url = (string?)null }));
        Assert.Equal(200, await Status("PATCH", $"/api/feed/{id}", author, new { body = "Back to text", url = (string?)null }));
        Assert.Contains(id, IdsOf(await All("kind=text", A, 50)));

        var listed = (await All("mine=true", A, 50)).Single(x => (string)x["id"]! == id);
        Assert.NotNull((string?)listed["edited_at"]);
        Assert.All((await All("", A, 50)).Where(x => (string)x["id"]! != id), x => Assert.Null(x["edited_at"]));
        Assert.Equal(1, await api.Count($"select count(*) from feed_posts where id = '{id}' and edited_at is not null"));
    }

    [Fact]
    public async Task F07_counters_follow_likes_unlikes_comments_and_deletes()
    {
        var id = Posts.Keys.First();
        var start = (await api.Call("GET", $"/api/feed/{id}", A)).Body;
        var likes = (int)start["like_count"]!;
        var comments = (int)start["comment_count"]!;

        var like = (await api.Call("POST", $"/api/feed/{id}/like", B)).Body; // B had not liked post 0 (0 likes in F01)
        Assert.Equal(likes + 1, (int)like["like_count"]!);
        Assert.True((bool)like["liked_by_me"]!);
        like = (await api.Call("POST", $"/api/feed/{id}/like", Co)).Body;
        Assert.Equal(likes + 2, (int)like["like_count"]!);
        like = (await api.Call("POST", $"/api/feed/{id}/like", B)).Body;
        Assert.Equal(likes + 1, (int)like["like_count"]!);
        Assert.False((bool)like["liked_by_me"]!);

        var c1 = (string)(await api.Call("POST", $"/api/feed/{id}/comments", B, new { body = "one" })).Body["id"]!;
        var c2 = (string)(await api.Call("POST", $"/api/feed/{id}/comments", Co, new { body = "two" })).Body["id"]!;
        Assert.Equal(comments + 2, (int)(await api.Call("GET", $"/api/feed/{id}", A)).Body["comment_count"]!);
        Assert.Equal(204, await Status("DELETE", $"/api/feed/comments/{c1}", B));
        Assert.Equal(204, await Status("DELETE", $"/api/feed/comments/{c2}", Admin));
        Assert.Equal(404, await Status("DELETE", $"/api/feed/comments/{c2}", Admin));
        var after = (await api.Call("GET", $"/api/feed/{id}", A)).Body;
        Assert.Equal(comments, (int)after["comment_count"]!);
        Assert.Equal(likes + 1, (int)after["like_count"]!);

        // The sorts follow the new counts at once.
        var topIds = IdsOf(await All("sort=top", A, 50));
        Assert.Equal(await Ids("select id from feed_posts p order by (select count(*) from feed_likes l where l.post_id = p.id) desc, created_at desc, id desc"), topIds);

        // Deleting a post takes its likes and comments with it; the other counters stay right.
        var busy = Posts.Keys.ElementAt(9); // 4 likes and 1 comment
        Assert.Equal(204, await Status("DELETE", $"/api/feed/{busy}", Admin));
        Assert.Equal(0, await api.Count($"select count(*) from feed_likes where post_id = '{busy}'"));
        Assert.Equal(404, await Status("GET", $"/api/feed/{busy}", A));
        Posts.Remove(busy);
        await CountersMatchRows();
        Assert.Equal(24, (await All("sort=discussed", A, 5)).Count);
    }
}
