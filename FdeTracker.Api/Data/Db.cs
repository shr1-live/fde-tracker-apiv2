using System.Data;
using Dapper;
using Npgsql;

namespace FdeTracker.Api.Data;

/// <summary>One open connection (optionally inside a transaction) handed to repositories.</summary>
public sealed class Session(NpgsqlConnection conn, NpgsqlTransaction? tx = null)
{
    public async Task<List<T>> Query<T>(string sql, object? args = null) => (await conn.QueryAsync<T>(sql, args, tx)).AsList();
    public Task<T?> First<T>(string sql, object? args = null) => conn.QueryFirstOrDefaultAsync<T>(sql, args, tx);
    public Task<int> Exec(string sql, object? args = null) => conn.ExecuteAsync(sql, args, tx);
    public Task<T> Scalar<T>(string sql, object? args = null) => conn.ExecuteScalarAsync<T>(sql, args, tx)!;
}

/// <summary>Opens pooled connections. Use <see cref="Tx"/> when several statements must succeed or fail together.</summary>
public sealed class Db(NpgsqlDataSource source)
{
    public async Task<T> Read<T>(Func<Session, Task<T>> work)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await work(new Session(conn));
    }

    public async Task<T> Tx<T>(Func<Session, Task<T>> work)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var result = await work(new Session(conn, tx));
        await tx.CommitAsync();
        return result; // an exception skips the commit; disposing the transaction rolls it back
    }

    public async Task Exec(string sql)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
