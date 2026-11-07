using MySqlConnector;
using Npgsql;

namespace JourneyPlanningApi.Data;

public enum DbProvider { MySql, PostgreSql }

/// <param name="Description">Connection target without credentials, safe to log.</param>
public sealed record DatabaseSettings(DbProvider Provider, string ConnectionString, string Description);

/// <summary>
/// Works out which database to use from configuration.
/// Precedence: <c>MYSQL_URL</c> / <c>DATABASE_URL</c> (mysql://... or postgres(ql)://..., e.g. the URL of a
/// Render PostgreSQL database), then <c>ConnectionStrings:Default</c> (MySQL), then the individual
/// <c>MYSQL_*</c> variables (same defaults as the Flask db.py). <c>MYSQL_SSL_MODE</c> overrides the MySQL SSL mode.
/// </summary>
public static class DatabaseConfig
{
    public static DatabaseSettings Build(IConfiguration cfg)
    {
        var url = FirstNonEmpty(cfg["MYSQL_URL"], cfg["DATABASE_URL"]);
        if (url is not null && IsPostgresUrl(url))
        {
            var pg = PostgresFromUrl(url);
            return new DatabaseSettings(DbProvider.PostgreSql, pg.ConnectionString,
                $"PostgreSQL {pg.Host}:{pg.Port}/{pg.Database} (user '{pg.Username}', ssl {pg.SslMode})");
        }

        var my = BuildMySql(cfg, url);
        return new DatabaseSettings(DbProvider.MySql, my.ConnectionString,
            $"MySQL {my.Server}:{my.Port}/{my.Database} (user '{my.UserID}', ssl {my.SslMode})");
    }

    private static MySqlConnectionStringBuilder BuildMySql(IConfiguration cfg, string? url)
    {
        MySqlConnectionStringBuilder cs;
        var raw = cfg.GetConnectionString("Default");

        if (url is not null)
            cs = MySqlFromUrl(url);
        else if (!string.IsNullOrWhiteSpace(raw))
            cs = new MySqlConnectionStringBuilder(raw);
        else
            cs = new MySqlConnectionStringBuilder
            {
                Server = FirstNonEmpty(cfg["MYSQL_HOST"]) ?? "localhost",
                Port = uint.TryParse(cfg["MYSQL_PORT"], out var port) ? port : 3306,
                UserID = FirstNonEmpty(cfg["MYSQL_USER"]) ?? "root",
                Password = cfg["MYSQL_PASSWORD"] ?? "",
                Database = FirstNonEmpty(cfg["MYSQL_DATABASE"]) ?? "journey_planning",
            };

        if (FirstNonEmpty(cfg["MYSQL_SSL_MODE"]) is { } ssl)
            cs.SslMode = ParseSslMode(ssl);

        return cs;
    }

    public static bool IsPostgresUrl(string url) =>
        url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    public static MySqlConnectionStringBuilder MySqlFromUrl(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme is not ("mysql" or "mysql2" or "mariadb"))
            throw new InvalidOperationException(
                $"Database URL scheme '{uri.Scheme}' is not supported; expected mysql://user:pass@host:port/db " +
                "or postgresql://user:pass@host:port/db");

        var (user, password) = UserInfo(uri);
        var cs = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = uri.Port > 0 ? (uint)uri.Port : 3306,
            UserID = user,
            Password = password,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        // Query parameters (e.g. ?ssl-mode=REQUIRED or ?sslmode=required) map onto connection string options.
        foreach (var (rawKey, value) in QueryParams(uri))
        {
            var key = rawKey.Replace("-", "").Replace("_", "");
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("ssl", StringComparison.OrdinalIgnoreCase))
                cs.SslMode = ParseSslMode(value);
            else
                cs[key] = value;
        }

        return cs;
    }

    public static NpgsqlConnectionStringBuilder PostgresFromUrl(string url)
    {
        var uri = new Uri(url);
        var (user, password) = UserInfo(uri);
        var cs = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = user,
            Password = password,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        foreach (var (key, value) in QueryParams(uri))
        {
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                cs.SslMode = Enum.Parse<SslMode>(value.Replace("-", "").Replace("_", ""), ignoreCase: true);
            // Other libpq-style parameters (channel_binding, application_name, ...) are not needed by Npgsql
            // or use different names, so they are ignored rather than failing startup.
        }

        return cs;
    }

    /// <summary>Accepts MySqlConnector names (VerifyCA) and MySQL CLI names (VERIFY_IDENTITY, DISABLED).</summary>
    public static MySqlSslMode ParseSslMode(string value) =>
        value.Replace("-", "").Replace("_", "").ToLowerInvariant() switch
        {
            "true" or "1" => MySqlSslMode.Required,
            "false" or "0" or "disabled" => MySqlSslMode.None,
            "verifyidentity" => MySqlSslMode.VerifyFull,
            var v when Enum.TryParse<MySqlSslMode>(v, ignoreCase: true, out var mode) => mode,
            _ => throw new InvalidOperationException(
                $"Unknown MySQL SSL mode '{value}'; use None, Preferred, Required, VerifyCA or VerifyFull"),
        };

    private static (string User, string Password) UserInfo(Uri uri)
    {
        var parts = uri.UserInfo.Split(':', 2);
        return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
    }

    private static IEnumerable<(string Key, string Value)> QueryParams(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(kv => (Uri.UnescapeDataString(kv[0]), kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : ""));

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
