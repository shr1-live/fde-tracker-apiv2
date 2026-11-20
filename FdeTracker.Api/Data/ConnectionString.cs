using Npgsql;

namespace FdeTracker.Api.Data;

public static class ConnectionString
{
    /// <summary>Accepts a postgres:// URL (as Supabase and Render give it) or a normal Npgsql connection string.</summary>
    public static string Normalize(string raw, int commandTimeoutSeconds = 20)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            // A ready-made connection string keeps what it says; if it does not set a limit, it gets ours.
            return raw.Contains("Command Timeout", StringComparison.OrdinalIgnoreCase) || raw.Contains("CommandTimeout", StringComparison.OrdinalIgnoreCase)
                ? raw : new NpgsqlConnectionStringBuilder(raw) { CommandTimeout = commandTimeoutSeconds }.ConnectionString;
        var uri = new Uri(raw);
        var user = uri.UserInfo.Split(':', 2);
        var local = uri.Host is "localhost" or "127.0.0.1";
        // Remote databases require SSL unless the URL says otherwise (?sslmode=disable, for a plain local Postgres).
        var sslMode = uri.Query.Contains("sslmode=disable", StringComparison.OrdinalIgnoreCase) || local ? SslMode.Disable : SslMode.Require;
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
            Database = uri.AbsolutePath.Trim('/'),
            MaxPoolSize = 5,
            CommandTimeout = commandTimeoutSeconds, // a database that hangs ends the command with an error instead of holding the request open
            SslMode = sslMode,
            GssEncryptionMode = GssEncryptionMode.Disable, // skip the Kerberos probe; the hosts we use do not offer it
        }.ConnectionString;
    }
}
