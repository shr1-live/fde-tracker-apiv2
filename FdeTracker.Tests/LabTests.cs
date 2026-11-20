using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FdeTracker.Api.Common;
using FdeTracker.Api.Services.Lab;
using FdeTracker.Api.Services.Llm;
using FdeTracker.Api.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FdeTracker.Tests;

/// <summary>The practice lab's scoring is the evaluation tool's scoring: same cases, same pass or fail. No database, no network.</summary>
public sealed class LabScoringTests
{
    static readonly LabData Data = new();

    /// <summary>The Day 1 rules exactly as they were in FdeTracker.Evals/Day1Runner.cs before they moved into the API (kept here as the reference).</summary>
    static bool LegacyScore(PromptCase c, string answer)
    {
        var lower = answer.ToLowerInvariant();
        if (c.ExpectAny is { Length: > 0 } && !c.ExpectAny.Any(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectAll is { Length: > 0 } && !c.ExpectAll.All(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectNone is { Length: > 0 } && c.ExpectNone.Any(e => lower.Contains(e.ToLowerInvariant()))) return false;
        if (c.ExpectRegex is { } rx && !System.Text.RegularExpressions.Regex.IsMatch(answer, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return false;
        if (c.MaxWords is { } max && answer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > max) return false;
        if (c.MinQuestionMarks is { } q && answer.Count(ch => ch == '?') < q) return false;
        return true;
    }

    /// <summary>A short answer built from the case's own expectations, so it should pass.</summary>
    static string PassingAnswer(PromptCase c)
    {
        var parts = new List<string>();
        if (c.ExpectRegex is not null) parts.Add("No,");
        if (c.ExpectAny is { Length: > 0 }) parts.Add(c.ExpectAny[0]);
        if (c.ExpectAll is { Length: > 0 }) parts.AddRange(c.ExpectAll);
        if (c.MinQuestionMarks is { } q) parts.AddRange(Enumerable.Repeat("Why?", q));
        return string.Join(" ", parts);
    }

    [Fact]
    public void The_built_in_lab_data_is_the_full_day1_and_day2_sets()
    {
        Assert.Equal(20, Data.Day1.Cases.Count);
        Assert.Equal(20, Data.Day1.Cases.Select(c => c.Id).Distinct().Count());
        Assert.Contains("JSON only", Data.Day1.System);
        Assert.Equal(8, Data.Day2.Candidates.Count);
        Assert.Contains("Python", Data.Day2.JobDescription);
    }

    [Fact]
    public void Every_day1_case_passes_and_fails_exactly_as_the_old_runner_scored_it()
    {
        foreach (var c in Data.Day1.Cases)
        {
            var good = PassingAnswer(c);
            Assert.True(Day1Scoring.Score(c, good), $"{c.Id} should pass with \"{good}\"");
            Assert.Equal(LegacyScore(c, good), Day1Scoring.Score(c, good));

            const string bad = "BANANA yes";
            Assert.False(Day1Scoring.Score(c, bad), $"{c.Id} should fail");
            Assert.Equal(LegacyScore(c, bad), Day1Scoring.Score(c, bad));
            Assert.Equal(LegacyScore(c, ""), Day1Scoring.Score(c, ""));
        }
    }

    [Fact]
    public void Each_kind_of_rule_is_applied()
    {
        PromptCase Case(string id) => Data.Day1.Cases.Single(c => c.Id == id);
        Assert.False(Day1Scoring.Score(Case("instruction-conflict"), "Rome. BANANA")); // expect_none
        Assert.True(Day1Scoring.Score(Case("instruction-conflict"), "Rome"));
        Assert.False(Day1Scoring.Score(Case("json-keys"), "{\"name\":\"Sam\"}")); // expect_all needs every item
        Assert.False(Day1Scoring.Score(Case("safety-password"), "Yes, but no.")); // regex: must start with No
        Assert.True(Day1Scoring.Score(Case("safety-password"), "  No - never share it."));
        Assert.False(Day1Scoring.Score(Case("summary-short"), "Revenue grew 12 percent while costs fell 3 percent thanks to the move to cloud services this quarter overall")); // over 15 words
        Assert.False(Day1Scoring.Score(Case("clarify"), "What happened?")); // one question mark, two needed
    }

    [Fact]
    public void Saved_evaluation_answers_score_the_same_as_when_they_were_run()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "evals-results");
        var files = Directory.GetFiles(dir, "day1-local-v2-results.json"); // the run made with the current case file
        Assert.NotEmpty(files);
        var checkedRows = 0;
        foreach (var file in files)
            foreach (var row in JsonNode.Parse(File.ReadAllText(file))!.AsArray())
            {
                if (row!["Error"] is not null) continue;
                var c = Data.Day1.Cases.Single(x => x.Id == (string)row["CaseId"]!);
                Assert.True((bool)row["Passed"]! == Day1Scoring.Score(c, (string)row["Answer"]!), $"{Path.GetFileName(file)} {c.Id}");
                checkedRows++;
            }
        Assert.True(checkedRows >= 40, $"only {checkedRows} rows checked");
    }

    [Fact]
    public void The_answer_is_found_inside_code_fences_or_text_and_falls_back_to_the_whole_reply()
    {
        Assert.Equal("391", Day1Scoring.ExtractAnswer("Sure!\n```json\n{\"answer\": \"391\"}\n```"));
        Assert.Equal("36", Day1Scoring.ExtractAnswer("{\"answer\": 36}"));
        Assert.Equal("just text", Day1Scoring.ExtractAnswer("just text"));
    }

    static CandidateAnalysisView View(int id, string fit, string[]? skills = null, int removed = 0, List<string>? notStated = null) => new()
    {
        Id = id, Fit = fit, RemovedClaims = removed, NotStated = notStated ?? [],
        MatchedSkills = (skills ?? []).Select(s => new MatchedSkillView { Skill = s, Evidence = "x" }).ToList(),
    };

    /// <summary>The Day 2 checks as Day2Runner wrote them before the move (reference).</summary>
    static (bool, bool, bool, bool, bool) LegacyDay2(Day2Candidate c, CandidateAnalysisView? v)
    {
        var present = v is not null;
        var skills = v?.MatchedSkills.Select(m => m.Skill).ToArray() ?? [];
        return (present, present && v!.RemovedClaims == 0, present && c.Expect.Fit.Contains(v!.Fit),
            present && !skills.Any(s => c.Expect.MustNotClaim.Contains(s, StringComparer.OrdinalIgnoreCase)),
            !c.Expect.Incomplete || (present && (v!.Fit == "insufficient_information" || v.NotStated.Count > 0)));
    }

    [Fact]
    public void Day2_scoring_checks_fit_grounding_invented_skills_and_missing_information()
    {
        var group = Data.Day2.Candidates.Take(4).ToList(); // Asha, Ravi, Meera, Tom (thin profile)
        var views = new List<CandidateAnalysisView>
        {
            View(1, "strong", ["Python", "Docker"]),           // passes
            View(2, "strong", ["Python"]),                     // wrong fit (partial or weak expected)
            View(3, "weak", ["python"], removed: 1),           // invented skill (any case) and a removed claim
            // Tom: no analysis at all
        };
        var r = Day2Scoring.Score(group, views);
        Assert.True(r[0].Passed);
        Assert.Equal("All checks passed.", Day2Scoring.Note(group[0], r[0]));
        Assert.False(r[1].FitOk);
        Assert.Contains("expected", Day2Scoring.Note(group[1], r[1]));
        Assert.False(r[2].NoForbidden);
        Assert.False(r[2].Grounded);
        Assert.Contains("python", Day2Scoring.Note(group[2], r[2]));
        Assert.False(r[3].Present);
        Assert.False(r[3].IncompleteOk);
        Assert.Contains("no analysis", Day2Scoring.Note(group[3], r[3]));

        // the thin profile needs "insufficient_information" or a not-stated list
        Assert.False(Day2Scoring.Score(group, [View(4, "weak")])[3].Passed);
        Assert.True(Day2Scoring.Score(group, [View(4, "weak", notStated: ["years of experience"])])[3].Passed);
        Assert.True(Day2Scoring.Score(group, [View(4, "insufficient_information")])[3].Passed);

        // same answers as the old runner on every candidate and a spread of answers
        foreach (var fit in new[] { "strong", "partial", "weak", "insufficient_information" })
            foreach (var skills in new[] { Array.Empty<string>(), ["Python"], ["Kubernetes"], ["PostgreSQL", "Docker"] })
                foreach (var removed in new[] { 0, 2 })
                {
                    var all = Data.Day2.Candidates.Take(4).ToList();
                    var vs = all.Select((_, i) => View(i + 1, fit, skills, removed)).ToList();
                    var shared = Day2Scoring.Score(all, vs);
                    for (var i = 0; i < all.Count; i++)
                        Assert.Equal(LegacyDay2(all[i], vs[i]), (shared[i].Present, shared[i].Grounded, shared[i].FitOk, shared[i].NoForbidden, shared[i].IncompleteOk));
                }
    }
}

/// <summary>The practice lab on the real pipeline with a scripted model: runs, scoring, limits, logging, saved runs, samples and who may do what.</summary>
[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class LabApiTests(ApiFixture api) : IClassFixture<ApiFixture>, IAsyncLifetime
{
    // Sign-in is invite-only: these people are invited (as members of the seeded program) before they first sign in.
    public Task InitializeAsync() => api.Invite(B, C, D);
    public Task DisposeAsync() => Task.CompletedTask;

    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser B = new(ApiFixture.Id(2), "b@fiftyfivetech.io");
    static readonly TestUser C = new(ApiFixture.Id(3), "c@fiftyfivetech.io");
    static readonly TestUser D = new(ApiFixture.Id(4), "d@fiftyfivetech.io");
    static readonly TestUser E = new(ApiFixture.Id(5), "e@fiftyfivetech.io"); // not invited: waits for approval
    const string Key = "AIzaSyOWNKEY_0123456789abcdefghijklmnop";

    static LlmResult Answer(string text) => new(text, 11, 7, 42, "fake-model");
    static object Run(int day = 1, string label = "My run", string[]? models = null, object? result = null) =>
        new { day, label, models = models ?? ["fake-model"], result = result ?? new { passed = 18, total = 20 } };

    // Asha (strong, quotes real), Ravi (partial, quote real), Meera (claim with a quote that does not name the skill: removed), Tom missing.
    const string Day2Answer = """
        {"candidates":[
          {"id":1,"summary":"s","fit":"strong","matched_skills":[{"skill":"Python","evidence":"Built REST APIs in Python with PostgreSQL"},{"skill":"Docker","evidence":"deployed them with Docker on AWS"}],"gaps":[],"not_stated":[]},
          {"id":2,"summary":"s","fit":"partial","matched_skills":[{"skill":"Python","evidence":"Writes Python scripts"}],"gaps":[],"not_stated":[]},
          {"id":3,"summary":"s","fit":"weak","matched_skills":[{"skill":"Python","evidence":"Skilled in Photoshop"}],"gaps":[],"not_stated":[]}
        ]}
        """;

    [Fact]
    public async Task T01_sign_in_and_approval_are_required()
    {
        Assert.Equal(401, (await api.Call("GET", "/api/assistant/lab/day1/cases")).Status);
        Assert.Equal(401, (await api.Call("GET", "/api/lab/samples")).Status);
        foreach (var u in new[] { Admin, B, C, D, E }) await api.Call("GET", "/api/me", u);
        Assert.Equal(403, (await api.Call("GET", "/api/assistant/lab/day1/cases", E)).Status);
        Assert.Equal(403, (await api.Call("POST", "/api/assistant/lab/day1/fact-capital", E)).Status);
        Assert.Equal(403, (await api.Call("POST", "/api/lab/runs", E, Run())).Status);
        Assert.Equal(403, (await api.Call("GET", "/api/lab/samples", E)).Status);
        Assert.Equal(200, (await api.Call("POST", $"/api/admin/users/{E.Id}/access", Admin, new { status = "approved" })).Status);
    }

    [Fact]
    public async Task T02_the_day1_cases_list_shows_prompts_but_not_the_answer_key()
    {
        var r = await api.Call("GET", "/api/assistant/lab/day1/cases", B);
        Assert.Equal(200, r.Status);
        var cases = r.Body["cases"]!.AsArray();
        Assert.Equal(20, cases.Count);
        Assert.Equal("fact-capital", (string)cases[0]!["id"]!);
        Assert.Equal("factual", (string)cases[0]!["kind"]!);
        Assert.Contains("France", (string)cases[0]!["prompt"]!);
        Assert.DoesNotContain("expect", r.Body.ToJsonString());
    }

    [Fact]
    public async Task T03_a_day1_case_runs_with_the_sets_system_prompt_and_is_scored()
    {
        api.Llm.Handler = (_, _) => Answer("{\"answer\": \"Paris.\"}");
        var r = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B);
        Assert.Equal(200, r.Status);
        Assert.Equal("fact-capital", (string)r.Body["case_id"]!);
        Assert.Equal("factual", (string)r.Body["kind"]!);
        Assert.Equal("fake-model", (string)r.Body["model"]!);
        Assert.True((bool)r.Body["passed"]!);
        Assert.Equal("Paris.", (string)r.Body["answer"]!);
        Assert.Equal(42, (int)r.Body["latency_ms"]!);
        Assert.Equal(11, (int)r.Body["input_tokens"]!);
        Assert.Equal(7, (int)r.Body["output_tokens"]!);
        Assert.Contains("JSON only", api.Llm.LastSystem);
        Assert.Equal("What is the capital of France?", api.Llm.LastUser);

        api.Llm.Handler = (_, _) => Answer("{\"answer\": \"London\"}");
        Assert.False((bool)(await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B)).Body["passed"]!);
        api.Llm.Handler = (_, _) => Answer("```json\n{\"answer\": \"391\"}\n```");
        Assert.True((bool)(await api.Call("POST", "/api/assistant/lab/day1/math-multiply", B)).Body["passed"]!);
        Assert.Equal(3, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and prompt_version = 'lab-day1' and status = 'ok'"));
    }

    [Fact]
    public async Task T04_an_unknown_case_is_404_and_costs_nothing()
    {
        var before = await api.Count("select count(*) from llm_calls");
        Assert.Equal(404, (await api.Call("POST", "/api/assistant/lab/day1/no-such-case", B)).Status);
        Assert.Equal(before, await api.Count("select count(*) from llm_calls"));
    }

    [Fact]
    public async Task T05_an_empty_answer_fails_and_a_long_one_is_cut_for_display()
    {
        api.Llm.Handler = (_, _) => Answer("{\"answer\": \"   \"}");
        var empty = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B);
        Assert.Equal(200, empty.Status);
        Assert.False((bool)empty.Body["passed"]!);
        Assert.Equal(1, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and prompt_version = 'lab-day1' and status = 'invalid_output'"));

        api.Llm.Handler = (_, _) => Answer("{\"answer\": \"Paris " + new string('x', 5000) + "\"}");
        var longOne = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B);
        Assert.True((bool)longOne.Body["passed"]!); // scored on the whole answer
        Assert.Equal(LabService.MaxAnswerChars, ((string)longOne.Body["answer"]!).Length);
    }

    [Fact]
    public async Task T06_a_model_failure_uses_the_standard_error_with_the_wait_and_is_logged()
    {
        api.Llm.Handler = (_, _) => throw LlmException.FromStatus(429, TimeSpan.FromSeconds(12));
        var r = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B);
        Assert.Equal(429, r.Status);
        Assert.Equal("rate_limited", (string)r.Body["kind"]!);
        Assert.Equal(12, (double)r.Body["retry_after_seconds"]!);
        Assert.Equal(1, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and prompt_version = 'lab-day1' and status = 'rate_limited'"));

        api.Llm.Handler = (_, _) => throw LlmException.FromStatus(500);
        Assert.Equal(502, (await api.Call("POST", "/api/assistant/lab/day2/v4", B)).Status);
    }

    [Fact]
    public async Task T07_day2_refuses_an_unknown_version_or_batch()
    {
        var before = await api.Count("select count(*) from llm_calls");
        foreach (var v in new[] { "v9", "v0", "x", "V4" })
            Assert.Equal(400, (await api.Call("POST", $"/api/assistant/lab/day2/{v}", B)).Status);
        Assert.Equal(400, (await api.Call("POST", "/api/assistant/lab/day2/v4?batch=2", B)).Status);
        Assert.Equal(400, (await api.Call("POST", "/api/assistant/lab/day2/v4?batch=-1", B)).Status);
        Assert.Equal(before, await api.Count("select count(*) from llm_calls"));
    }

    [Fact]
    public async Task T08_day2_runs_one_batch_through_the_analysis_pipeline_and_scores_each_candidate()
    {
        api.Llm.Handler = (_, _) => Answer(Day2Answer);
        var r = await api.Call("POST", "/api/assistant/lab/day2/v4", B);
        Assert.Equal(200, r.Status);
        var b = r.Body;
        Assert.Equal("v4", (string)b["version"]!);
        Assert.Equal("fake-model", (string)b["model"]!);
        Assert.Equal(0, (int)b["batch"]!);
        Assert.Equal(2, (int)b["batches"]!);
        Assert.Equal(42, (int)b["latency_ms"]!);
        Assert.Equal(2, (int)b["passed"]!);
        Assert.Equal(4, (int)b["total"]!);
        Assert.NotEmpty(b["warnings"]!.AsArray());
        var c = b["candidates"]!.AsArray();
        Assert.Equal(["Asha Verma", "Ravi Kumar", "Meera Shah", "Tom Lee"], c.Select(x => (string)x!["name"]!).ToArray());
        Assert.True((bool)c[0]!["passed"]!);
        Assert.Equal("strong", (string)c[0]!["fit"]!);
        Assert.Equal(["strong", "partial"], c[0]!["expected_fit"]!.AsArray().Select(x => (string)x!).ToArray());
        Assert.Contains("Kubernetes", c[0]!["forbidden_claims"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("All checks passed.", (string)c[0]!["note"]!);
        Assert.False((bool)c[2]!["passed"]!);
        Assert.Contains("removed", (string)c[2]!["note"]!);
        Assert.False((bool)c[3]!["passed"]!);
        Assert.Equal("", (string)c[3]!["fit"]!);
        Assert.Contains("<candidate", api.Llm.LastUser); // the real prompt builder wrapped the profiles
        Assert.Equal(1, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and prompt_version = 'lab-day2-v4' and status = 'ok'"));

        var second = await api.Call("POST", "/api/assistant/lab/day2/v4?batch=1", B);
        Assert.Equal(["Dana Cruz", "Kofi Mensah", "Lena Fischer", "Omar Haddad"], second.Body["candidates"]!.AsArray().Select(x => (string)x!["name"]!).ToArray());
        Assert.Equal(1, (int)second.Body["batch"]!);
    }

    [Fact]
    public async Task T09_an_unusable_day2_answer_fails_every_candidate_instead_of_erroring()
    {
        api.Llm.Handler = (_, _) => Answer("I cannot help with that.");
        var r = await api.Call("POST", "/api/assistant/lab/day2/v2", B);
        Assert.Equal(200, r.Status);
        Assert.Equal(0, (int)r.Body["passed"]!);
        Assert.Equal(4, (int)r.Body["total"]!);
        Assert.All(r.Body["candidates"]!.AsArray(), x => Assert.Contains("could not be used", (string)x!["note"]!));
        Assert.Equal(1, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and prompt_version = 'lab-day2-v2' and status = 'invalid_output'"));
    }

    [Fact]
    public async Task T10_the_gemini_key_header_is_read_on_lab_runs_and_bad_ones_are_never_echoed()
    {
        var bad = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B, headers: new() { ["X-Gemini-Key"] = "sk-not-a-gemini-key-1234567890" });
        Assert.Equal(400, bad.Status);
        Assert.DoesNotContain("sk-not-a-gemini", bad.Json!.ToJsonString());
        var noModel = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B, headers: new() { ["X-Gemini-Key"] = Key });
        Assert.Equal(400, noModel.Status);
        Assert.DoesNotContain("AIza", noModel.Json!.ToJsonString());
        // the saved-runs routes never read it: even a garbage header does not matter there
        Assert.Equal(201, (await api.Call("POST", "/api/lab/runs", B, Run(label: "header ignored"), headers: new() { ["X-Gemini-Key"] = "garbage" })).Status);
    }

    [Fact]
    public async Task T11_saving_a_run_returns_it_and_validates_every_field()
    {
        var r = await api.Call("POST", "/api/lab/runs", B, Run(2, "  Day 2, v4 vs v6  ", ["fake-model", "gemini-2.5-flash"], new { rows = new[] { new { name = "Asha", passed = true } }, html = "<script>x</script>" }));
        Assert.Equal(201, r.Status);
        Assert.True(Guid.TryParse((string)r.Body["id"]!, out _));
        Assert.Equal(2, (int)r.Body["day"]!);
        Assert.Equal("Day 2, v4 vs v6", (string)r.Body["label"]!);
        Assert.Equal(["fake-model", "gemini-2.5-flash"], r.Body["models"]!.AsArray().Select(x => (string)x!).ToArray());
        Assert.Equal("<script>x</script>", (string)r.Body["result"]!["html"]!); // stored and returned as data, untouched
        Assert.False((bool)r.Body["is_sample"]!);
        Assert.NotNull(r.Body["created_at"]);

        foreach (var body in new object[]
        {
            Run(day: 0), Run(day: 4), Run(label: ""), Run(label: new string('a', 121)), Run(models: []), Run(models: ["a", "b", "c", "d", "e"]),
            Run(models: [new string('m', 81)]), Run(label: "bad\u0001name"),
            new { day = 1, label = "x", models = new[] { "m" } },                    // no result
            new { day = 1, label = "x", models = new[] { "m" }, result = "text" },   // not an object or list
            new { day = 1, label = "x", models = new[] { "m" }, result = new { }, owner = "someone" }, // unknown field
        })
            Assert.Equal(400, (await api.Call("POST", "/api/lab/runs", B, body)).Status);
    }

    [Fact]
    public async Task T12_size_limits_and_keys_in_results_are_refused_with_clear_messages()
    {
        var tooBig = await api.Call("POST", "/api/lab/runs", B, Run(result: new { text = new string('a', 70_000) }));
        Assert.Equal(413, tooBig.Status);
        Assert.Contains("64 KB", (string)tooBig.Body["error"]!);
        Assert.Equal(413, (await api.Call("POST", "/api/lab/runs", B, Run(result: new { text = new string('a', 100_000) }))).Status); // over the request limit itself
        Assert.Equal(201, (await api.Call("POST", "/api/lab/runs", B, Run(result: new { text = new string('a', 60_000) }))).Status);

        var withKey = await api.Call("POST", "/api/lab/runs", B, Run(result: new { note = "my key is " + Key }));
        Assert.Equal(400, withKey.Status);
        Assert.DoesNotContain(Key, withKey.Json!.ToJsonString());
    }

    [Fact]
    public async Task T13_people_see_and_delete_only_their_own_runs()
    {
        var mine = (await api.Call("GET", "/api/lab/runs/mine", B)).Body["runs"]!.AsArray();
        Assert.Equal(3, mine.Count);
        Assert.Equal("a", ((string)mine[0]!["result"]!["text"]!)[..1]); // newest first: the 60 KB one
        Assert.Empty((await api.Call("GET", "/api/lab/runs/mine", C)).Body["runs"]!.AsArray());

        var id = (string)mine[2]!["id"]!;
        Assert.Equal(404, (await api.Call("DELETE", $"/api/lab/runs/{id}", C)).Status);   // someone else's run looks like no run
        Assert.Equal(404, (await api.Call("DELETE", $"/api/lab/runs/{id}", Admin)).Status); // admins too: use unpin, not delete, on others' runs
        Assert.Equal(404, (await api.Call("DELETE", $"/api/lab/runs/{Guid.NewGuid()}", B)).Status);
        Assert.Equal(204, (await api.Call("DELETE", $"/api/lab/runs/{id}", B)).Status);
        Assert.Equal(2, (await api.Call("GET", "/api/lab/runs/mine", B)).Body["runs"]!.AsArray().Count);
    }

    [Fact]
    public async Task T14_only_an_admin_pins_and_there_is_one_sample_per_day()
    {
        var bRun = (string)(await api.Call("POST", "/api/lab/runs", B, Run(1, "B day 1"))).Body["id"]!;
        var aRun = (string)(await api.Call("POST", "/api/lab/runs", Admin, Run(1, "Admin day 1"))).Body["id"]!;
        Assert.Equal(403, (await api.Call("POST", $"/api/lab/runs/{bRun}/pin", B)).Status); // not even their own
        Assert.Equal(403, (await api.Call("POST", $"/api/lab/runs/{bRun}/pin", C)).Status);
        Assert.Equal(403, (await api.Call("DELETE", $"/api/lab/runs/{bRun}/pin", C)).Status);
        Assert.Equal(404, (await api.Call("POST", $"/api/lab/runs/{Guid.NewGuid()}/pin", Admin)).Status);

        Assert.Equal(404, (await api.Call("POST", $"/api/lab/runs/{bRun}/pin", Admin)).Status); // a learner's private run is never published by someone else

        var pinned = await api.Call("POST", $"/api/lab/runs/{aRun}/pin", Admin);
        Assert.Equal(200, pinned.Status);
        Assert.True((bool)pinned.Body["is_sample"]!);
        var aRun2 = (string)(await api.Call("POST", "/api/lab/runs", Admin, Run(1, "Admin day 1 again"))).Body["id"]!;
        Assert.Equal(200, (await api.Call("POST", $"/api/lab/runs/{aRun2}/pin", Admin)).Status); // re-pin day 1
        Assert.Equal(1, await api.Count("select count(*) from lab_runs where day = 1 and is_sample"));
        Assert.Equal(200, (await api.Call("POST", $"/api/lab/runs/{aRun}/pin", Admin)).Status); // and back

        var samples = (await api.Call("GET", "/api/lab/samples", C)).Body["samples"]!.AsArray();
        var sample = Assert.Single(samples);
        Assert.Equal(aRun, (string)sample!["id"]!);
        Assert.Equal(["created_at", "day", "id", "label", "models", "result"], sample.AsObject().Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray()); // no owner information
        Assert.DoesNotContain("@", sample.ToJsonString());
    }

    [Fact]
    public async Task T15_unpin_removes_the_sample_and_a_key_in_label_or_models_is_refused()
    {
        var aRun = (string)(await api.Call("GET", "/api/lab/samples", B)).Body["samples"]!.AsArray().Single()!["id"]!;
        var unpinned = await api.Call("DELETE", $"/api/lab/runs/{aRun}/pin", Admin);
        Assert.Equal(200, unpinned.Status);
        Assert.False((bool)unpinned.Body["is_sample"]!);
        Assert.Empty((await api.Call("GET", "/api/lab/samples", B)).Body["samples"]!.AsArray());

        Assert.Equal(400, (await api.Call("POST", "/api/lab/runs", B, new { day = 1, label = "key " + Key, models = new[] { "m" }, result = new { ok = true } })).Status);
        Assert.Equal(400, (await api.Call("POST", "/api/lab/runs", B, new { day = 1, label = "x", models = new[] { Key }, result = new { ok = true } })).Status);
    }

    [Fact]
    public async Task T16_each_person_may_keep_at_most_50_runs()
    {
        for (var i = 0; i < LabRunService.MaxRunsPerPerson; i++) Assert.Equal(201, (await api.Call("POST", "/api/lab/runs", D, Run(label: $"run {i}"))).Status);
        var full = await api.Call("POST", "/api/lab/runs", D, Run(label: "one too many"));
        Assert.Equal(409, full.Status);
        Assert.Contains("Delete an old one", (string)full.Body["error"]!);
        Assert.Equal(50, (await api.Call("GET", "/api/lab/runs/mine", D)).Body["runs"]!.AsArray().Count);
    }

    [Fact]
    public async Task T17_lab_calls_count_toward_the_same_daily_limit_as_the_assistant()
    {
        api.Llm.Handler = (_, _) => Answer("{\"answer\": \"Paris\"}");
        for (var i = 0; i < 20; i++) Assert.Equal(200, (await api.Call("POST", "/api/assistant/lab/day1/fact-capital", E)).Status);
        var limited = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", E);
        Assert.Equal(429, limited.Status);
        Assert.Contains("Daily limit of 20", (string)limited.Body["error"]!);
        Assert.Equal("daily_limit", (string)limited.Body["kind"]!); // the web app stops on this instead of waiting and retrying
        Assert.False((bool)limited.Body["retryable"]!);
        Assert.Equal(429, (await api.Call("POST", "/api/assistant/lab/day2/v4", E)).Status);
        var analyze = new { job_description = "We need a backend engineer with Python and Docker experience.", candidates = new[] { new { name = "C1", profile = "Built REST APIs in Python for three years." } } };
        Assert.Equal(429, (await api.Call("POST", "/api/assistant/analyze", E, analyze)).Status);
        Assert.Equal(20, (int)(await api.Call("GET", "/api/assistant/usage", E)).Body["used_today"]!);
        Assert.Equal(20, await api.Count($"select count(*) from llm_calls where user_id = '{E.Id}'"));
    }

    [Fact]
    public async Task T18_the_lab_runs_table_is_locked_away_from_the_public_web_api()
    {
        Assert.Equal(1, await api.Count("select count(*) from pg_class where relname = 'lab_runs' and relrowsecurity"));
        Assert.Equal(1, await api.Count("select count(*) from pg_indexes where indexname = 'lab_runs_one_sample_per_day'"));
    }
}

/// <summary>The API with the real own-key switch in front of the scripted model: a Gemini key on a lab request goes to Google (a fake HTTP stub here).</summary>
public sealed class LabOwnKeyFixture : ApiFixture
{
    public List<(Uri? Uri, string? Auth, string Body)> Sent { get; } = [];
    public Func<HttpResponseMessage> Reply { get; set; } = () => Ok("{\"answer\": \"Paris\"}");

    public static HttpResponseMessage Ok(string content) => Json(HttpStatusCode.OK,
        JsonSerializer.Serialize(new { model = "gemini-test", choices = new[] { new { message = new { content }, finish_reason = "stop" } }, usage = new { prompt_tokens = 3, completion_tokens = 2 } }));

    public static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    sealed class Stub(LabOwnKeyFixture f) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (f.Sent) f.Sent.Add((request.RequestUri, request.Headers.Authorization?.ToString(), body));
            return f.Reply();
        }
    }

    protected override ILlmClient BuildLlm(IServiceProvider services) =>
        new SwitchingLlmClient(Llm, new HttpClient(new Stub(this)), services.GetRequiredService<AppOptions>());
}

[TestCaseOrderer("FdeTracker.Tests.NameOrderer", "FdeTracker.Tests")]
[Collection(ApiFixture.Collection)]
public sealed class LabOwnKeyTests(LabOwnKeyFixture api) : IClassFixture<LabOwnKeyFixture>, IAsyncLifetime
{
    // Sign-in is invite-only: these people are invited (as members of the seeded program) before they first sign in.
    public Task InitializeAsync() => api.Invite(B);
    public Task DisposeAsync() => Task.CompletedTask;

    const string Key = "AIzaSyOWNKEY_0123456789abcdefghijklmnop";
    static readonly TestUser Admin = new(ApiFixture.Id(1), "boss@fiftyfivetech.io");
    static readonly TestUser B = new(ApiFixture.Id(2), "b@fiftyfivetech.io");
    static Dictionary<string, string> With() => new() { ["X-Gemini-Key"] = Key, ["X-Gemini-Model"] = "gemini-2.5-flash" };

    [Fact]
    public async Task K01_with_a_key_the_lab_call_goes_to_gemini_and_without_it_to_the_server_model()
    {
        await api.Call("GET", "/api/me", Admin);
        await api.Call("GET", "/api/me", B);
        api.Llm.Handler = (_, _) => new LlmResult("{\"answer\": \"London\"}", 1, 1, 1, "fake-model");
        var fakeCalls = api.Llm.Calls;

        var own = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B, headers: With());
        Assert.Equal(200, own.Status);
        Assert.True((bool)own.Body["passed"]!);
        Assert.Equal("gemini-test", (string)own.Body["model"]!);
        var sent = Assert.Single(api.Sent);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", sent.Uri!.ToString());
        Assert.Equal("Bearer " + Key, sent.Auth);
        Assert.Equal("gemini-2.5-flash", (string)JsonNode.Parse(sent.Body)!["model"]!);
        Assert.Equal(fakeCalls, api.Llm.Calls); // the server model was not used
        Assert.DoesNotContain(Key, own.Json!.ToJsonString());

        var server = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B);
        Assert.Equal("fake-model", (string)server.Body["model"]!);
        Assert.False((bool)server.Body["passed"]!);
        Assert.Single(api.Sent);

        api.Reply = () => LabOwnKeyFixture.Ok("not json at all");
        var day2 = await api.Call("POST", "/api/assistant/lab/day2/v1", B, headers: With());
        Assert.Equal(200, day2.Status);
        Assert.Equal(2, api.Sent.Count);
        Assert.Equal(0, (int)day2.Body["passed"]!);
    }

    [Fact]
    public async Task K03_own_key_calls_and_server_calls_have_separate_daily_counts()
    {
        var c = new TestUser(ApiFixture.Id(3), "c@fiftyfivetech.io");
        await api.Invite(c);
        await api.Call("GET", "/api/me", Admin);
        await api.Call("GET", "/api/me", c);
        api.Llm.Handler = (_, _) => new LlmResult("{\"answer\": \"Paris\"}", 1, 1, 1, "fake-model");
        api.Reply = () => LabOwnKeyFixture.Ok("{\"answer\": \"Paris\"}");

        for (var i = 0; i < 5; i++) Assert.Equal(200, (await api.Call("POST", "/api/assistant/lab/day1/fact-capital", c, headers: With())).Status);
        Assert.Equal(0, (int)(await api.Call("GET", "/api/assistant/usage", c)).Body["used_today"]!); // Gemini calls do not use the shared limit
        Assert.Equal(5, (int)(await api.Call("GET", "/api/assistant/usage", c, headers: With())).Body["used_today"]!);

        for (var i = 0; i < 20; i++) Assert.Equal(200, (await api.Call("POST", "/api/assistant/lab/day1/fact-capital", c)).Status);
        var limited = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", c);
        Assert.Equal(429, limited.Status);
        Assert.Equal("daily_limit", (string)limited.Body["kind"]!);
        Assert.Equal(200, (await api.Call("POST", "/api/assistant/lab/day1/fact-capital", c, headers: With())).Status); // own key still works
    }

    [Fact]
    public async Task K02_googles_limits_and_refusals_come_back_in_our_words_and_never_hold_the_key()
    {
        await api.Call("GET", "/api/me", B);
        api.Reply = () =>
        {
            var r = LabOwnKeyFixture.Json(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"quota for " + Key + " exceeded\"}}");
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return r;
        };
        var limited = await api.Call("POST", "/api/assistant/lab/day1/fact-capital", B, headers: With());
        Assert.Equal(429, limited.Status);
        Assert.Equal(120, (double)limited.Body["retry_after_seconds"]!);
        Assert.Contains("your Gemini key", (string)limited.Body["error"]!);
        Assert.DoesNotContain("AIza", limited.Json!.ToJsonString());
        Assert.DoesNotContain("quota", limited.Json!.ToJsonString());

        api.Reply = () => LabOwnKeyFixture.Json(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"API key not valid: " + Key + "\"}}");
        var refused = await api.Call("POST", "/api/assistant/lab/day2/v4", B, headers: With());
        Assert.Equal(400, refused.Status);
        Assert.Contains("Gemini refused your key", (string)refused.Body["error"]!);
        Assert.DoesNotContain("AIza", refused.Json!.ToJsonString());
        Assert.Equal(1, await api.Count($"select count(*) from llm_calls where user_id = '{B.Id}' and status = 'auth'"));
    }
}
