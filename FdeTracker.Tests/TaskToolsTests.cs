using System.Text.Json;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.Services.Tasks;
using Npgsql;

namespace FdeTracker.Tests;

/// <summary>
/// The tools on their own, with no database at all: bad arguments are refused before anything is touched, and a database
/// that cannot be reached becomes a plain "failed" with nothing leaked.
/// </summary>
public sealed class TaskToolsTests
{
    static readonly NpgsqlDataSource Dead = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=1;Command Timeout=1;Pooling=false");
    readonly TaskTools tools = new(new Db(Dead), new TaskRepository());
    static readonly AppUser Person = new() { Id = Guid.NewGuid(), Email = "p@fiftyfivetech.io", Role = "member", AccessStatus = "approved" };

    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static TheoryData<string, string> Refused => new()
    {
        { "create_task", "{}" },                                                  // title missing
        { "create_task", """{"title":""}""" },
        { "create_task", """{"title":42}""" },                                    // wrong type
        { "create_task", "{\"title\":\"" + new string('x', 141) + "\"}" },        // too long
        { "create_task", """{"title":"ok","priority":"urgent"}""" },              // not an allowed value
        { "create_task", """{"title":"ok","due_date":"2026-02-30"}""" },          // not a real date
        { "create_task", """{"title":"ok","due_date":"tomorrow"}""" },            // not the agreed format
        { "create_task", """{"title":"ok","due_date":"1999-01-01"}""" },          // outside the allowed years
        { "create_task", "{\"title\":\"ok\",\"notes\":\"" + new string('n', 501) + "\"}" },
        { "create_task", """{"title":"ok","owner":"someone-else"}""" },           // unknown argument
        { "create_task", """{"title":"ok","user_id":"11111111-1111-4111-8111-111111111111"}""" }, // the model cannot name a person
        { "create_task", "[1,2]" },                                               // arguments must be an object
        { "update_task", "{}" },                                                  // no id
        { "update_task", """{"id":"not-a-guid","title":"x"}""" },
        { "update_task", """{"id":"11111111-1111-4111-8111-111111111111"}""" },  // nothing to change
        { "update_task", """{"id":"11111111-1111-4111-8111-111111111111","status":"finished"}""" },
        { "update_task", """{"id":"11111111-1111-4111-8111-111111111111","title":null}""" }, // a title cannot be cleared
        { "list_tasks", """{"limit":0}""" },
        { "list_tasks", """{"limit":51}""" },
        { "list_tasks", """{"limit":"ten"}""" },
        { "list_tasks", """{"status":"everything"}""" },
        { "delete_task", "{}" },
        { "delete_task", """{"id":"1"}""" },
        { "drop_database", "{}" },                                                // not a tool
    };

    [Theory, MemberData(nameof(Refused))]
    public async Task Bad_arguments_are_refused_before_any_database_is_touched(string tool, string args)
    {
        var r = await tools.Execute(Person, tool, J(args), confirmed: false);
        Assert.Equal("invalid", r.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(r.Message)); // the model is told what to fix
    }

    [Fact]
    public async Task A_database_that_cannot_be_reached_becomes_a_plain_failure_and_nothing_leaks()
    {
        var r = await tools.Execute(Person, "create_task", J("""{"title":"Valid task"}"""), confirmed: false);
        Assert.Equal("failed", r.Outcome);
        Assert.Equal("The tool failed and nothing was changed.", r.Message);
        Assert.DoesNotContain("127.0.0.1", r.Message);
        Assert.DoesNotContain("Npgsql", r.Message);
    }

    [Fact]
    public void Only_delete_is_destructive()
    {
        Assert.True(TaskTools.IsDestructive("delete_task"));
        Assert.All(new[] { "create_task", "list_tasks", "update_task" }, t => Assert.False(TaskTools.IsDestructive(t)));
    }
}
