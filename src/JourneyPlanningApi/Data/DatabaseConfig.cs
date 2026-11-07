using MySqlConnector;

namespace JourneyPlanningApi.Data;

/// <summary>
/// Builds the MySQL connection string from configuration.
/// Precedence: <c>MYSQL_URL</c> / <c>DATABASE_URL</c> (mysql://user:pass@host:port/db?ssl-mode=Required),
/// then <c>ConnectionStrings:Default</c>, then the individual <c>MYSQL_*</c> variables
/// (same defaults as the Flask db.py). <c>MYSQL_SSL_MODE</c> overrides the SSL mode in every case.
/// </summary>
public static class DatabaseConfig
{
    public static MySqlConnectionStringBuilder Build(IConfiguration cfg)
    {
        MySqlConnectionStringBuilder cs;

        var url = FirstNonEmpty(cfg["MYSQL_URL"], cfg["DATABASE_URL"]);
        var raw = cfg.GetConnectionString("Default");

        if (url is not null)
            cs = FromUrl(url);
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

    public static MySqlConnectionStringBuilder FromUrl(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme is not ("mysql" or "mysql2" or "mariadb"))
            throw new InvalidOperationException(
                $"Database URL scheme '{uri.Scheme}' is not supported; expected mysql://user:pass@host:port/db");

        var userInfo = uri.UserInfo.Split(':', 2);
        var cs = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = uri.Port > 0 ? (uint)uri.Port : 3306,
            UserID = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        // Query parameters (e.g. ?ssl-mode=REQUIRED or ?sslmode=required) map onto connection string options.
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).Replace("-", "").Replace("_", "");
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("ssl", StringComparison.OrdinalIgnoreCase))
                cs.SslMode = ParseSslMode(value);
            else
                cs[key] = value;
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

    /// <summary>Connection target without credentials, safe to log.</summary>
    public static string Describe(MySqlConnectionStringBuilder cs) =>
        $"{cs.Server}:{cs.Port}/{cs.Database} (user '{cs.UserID}', ssl {cs.SslMode})";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
