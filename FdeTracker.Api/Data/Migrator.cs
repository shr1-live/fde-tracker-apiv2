using Dapper;

namespace FdeTracker.Api.Data;

/// <summary>Applies the .sql files in migrations/ once each, in name order.</summary>
public static class Migrator
{
    public static async Task RunAsync(Db db, ILogger logger)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "migrations");
        await db.Exec("create table if not exists schema_migrations (name text primary key, applied_at timestamptz not null default now())");
        var done = (await db.Read(s => s.Query<string>("select name from schema_migrations"))).ToHashSet();
        foreach (var file in Directory.GetFiles(dir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (done.Contains(name)) continue;
            await db.Exec(await File.ReadAllTextAsync(file));
            await db.Read(s => s.Exec("insert into schema_migrations (name) values (@name)", new { name }));
            logger.LogInformation("Applied migration {Name}", name);
        }
    }
}
