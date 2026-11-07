using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Data;

/// <summary>
/// EnsureCreated only creates tables in an empty database; it never alters existing ones. This adds the email
/// verification / password reset columns (Flask deploy branch) to a users table created before they existed.
/// Accounts that predate the is_verified column are marked verified so existing users are not locked out.
/// Safe to run on every start: it only adds columns that are missing.
/// </summary>
public static class SchemaUpgrade
{
    public static async Task<IReadOnlyList<string>> AddMissingUserColumnsAsync(AppDbContext db, DbProvider provider)
    {
        var pg = provider == DbProvider.PostgreSql;
        var columns = new (string Name, string Type)[]
        {
            ("is_verified", pg ? "boolean NOT NULL DEFAULT FALSE" : "tinyint(1) NOT NULL DEFAULT 0"),
            ("verification_token", "varchar(255) NULL"),
            ("verification_expires", pg ? "timestamp without time zone NULL" : "datetime(6) NULL"),
            ("reset_token", "varchar(255) NULL"),
            ("reset_expires", pg ? "timestamp without time zone NULL" : "datetime(6) NULL"),
        };

        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync();
        try
        {
            var existing = await ExistingColumnsAsync(conn, pg);
            var added = new List<string>();
            foreach (var (name, type) in columns)
            {
                if (existing.Contains(name)) continue;
                await ExecAsync(conn, $"ALTER TABLE users ADD COLUMN {name} {type}");
                added.Add(name);
            }
            if (added.Contains("is_verified"))
                await ExecAsync(conn, pg ? "UPDATE users SET is_verified = TRUE" : "UPDATE users SET is_verified = 1");
            return added;
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(DbConnection conn, bool pg)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = pg
            ? "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'users'"
            : "SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users'";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task ExecAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
