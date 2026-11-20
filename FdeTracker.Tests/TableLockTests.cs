using Npgsql;

namespace FdeTracker.Tests;

/// <summary>Every backend table has row level security on, so Supabase's public web API (anon key) cannot read or change it. The API still works (the other tests prove that).</summary>
[Collection(ApiFixture.Collection)]
public sealed class TableLockTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Every_backend_table_has_row_level_security_on()
    {
        Assert.Equal(200, (await api.Call("GET", "/health")).Status); // the API (and its migrations) has started
        await using var conn = new NpgsqlConnection(api.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'public' and c.relkind = 'r' and not c.relrowsecurity order by 1", conn);
        var open = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) open.Add(r.GetString(0));
        Assert.Empty(open); // a new table without RLS fails this test: add it to a migration like 008
    }
}
