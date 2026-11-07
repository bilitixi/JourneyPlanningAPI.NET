using JourneyPlanningApi.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class DatabaseConfigTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static MySqlConnectionStringBuilder MySql(IConfiguration cfg)
    {
        var settings = DatabaseConfig.Build(cfg);
        Assert.Equal(DbProvider.MySql, settings.Provider);
        return new MySqlConnectionStringBuilder(settings.ConnectionString);
    }

    [Fact]
    public void Defaults_match_flask_db_py()
    {
        var cs = MySql(Config());
        Assert.Equal("localhost", cs.Server);
        Assert.Equal(3306u, cs.Port);
        Assert.Equal("root", cs.UserID);
        Assert.Equal("", cs.Password);
        Assert.Equal("journey_planning", cs.Database);
    }

    [Fact]
    public void Individual_variables_are_used()
    {
        var cs = MySql(Config(
            ("MYSQL_HOST", "db.example.com"), ("MYSQL_PORT", "25060"), ("MYSQL_USER", "app"),
            ("MYSQL_PASSWORD", "secret"), ("MYSQL_DATABASE", "journeys"), ("MYSQL_SSL_MODE", "required")));
        Assert.Equal("db.example.com", cs.Server);
        Assert.Equal(25060u, cs.Port);
        Assert.Equal("app", cs.UserID);
        Assert.Equal("secret", cs.Password);
        Assert.Equal("journeys", cs.Database);
        Assert.Equal(MySqlSslMode.Required, cs.SslMode);
    }

    [Fact]
    public void Url_takes_precedence_and_is_decoded()
    {
        var cs = MySql(Config(
            ("MYSQL_HOST", "ignored"),
            ("MYSQL_URL", "mysql://app:p%40ss%3Aword@db.example.com:25060/journeys?ssl-mode=REQUIRED")));
        Assert.Equal("db.example.com", cs.Server);
        Assert.Equal(25060u, cs.Port);
        Assert.Equal("app", cs.UserID);
        Assert.Equal("p@ss:word", cs.Password);
        Assert.Equal("journeys", cs.Database);
        Assert.Equal(MySqlSslMode.Required, cs.SslMode);
    }

    [Fact]
    public void Database_url_without_port_defaults_to_3306()
    {
        var cs = MySql(Config(("DATABASE_URL", "mysql://app:pw@db.example.com/journeys")));
        Assert.Equal(3306u, cs.Port);
        Assert.Equal("journeys", cs.Database);
    }

    [Fact]
    public void Unsupported_url_is_rejected_with_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfig.Build(Config(("DATABASE_URL", "sqlserver://u:p@host:1433/db"))));
        Assert.Contains("postgresql://", ex.Message);
    }

    [Theory]
    [InlineData("postgresql://app:p%40ss@dpg-abc123-a/journeys", "dpg-abc123-a", 5432, SslMode.Prefer)]
    [InlineData("postgres://app:p%40ss@dpg-abc123-a.oregon-postgres.render.com:6543/journeys?sslmode=require",
        "dpg-abc123-a.oregon-postgres.render.com", 6543, SslMode.Require)]
    [InlineData("postgresql://app:p%40ss@db.example.com/journeys?sslmode=verify-full&channel_binding=require",
        "db.example.com", 5432, SslMode.VerifyFull)]
    public void Postgres_url_selects_postgres(string url, string host, int port, SslMode ssl)
    {
        var settings = DatabaseConfig.Build(Config(("MYSQL_HOST", "ignored"), ("DATABASE_URL", url)));
        Assert.Equal(DbProvider.PostgreSql, settings.Provider);
        var cs = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
        Assert.Equal(host, cs.Host);
        Assert.Equal(port, cs.Port);
        Assert.Equal("app", cs.Username);
        Assert.Equal("p@ss", cs.Password);
        Assert.Equal("journeys", cs.Database);
        Assert.Equal(ssl, cs.SslMode);
        Assert.DoesNotContain("p@ss", settings.Description);
    }

    [Theory]
    [InlineData("REQUIRED", MySqlSslMode.Required)]
    [InlineData("disabled", MySqlSslMode.None)]
    [InlineData("VERIFY_CA", MySqlSslMode.VerifyCA)]
    [InlineData("VERIFY_IDENTITY", MySqlSslMode.VerifyFull)]
    [InlineData("true", MySqlSslMode.Required)]
    public void Ssl_mode_accepts_connector_and_cli_names(string value, MySqlSslMode expected) =>
        Assert.Equal(expected, DatabaseConfig.ParseSslMode(value));

    [Fact]
    public void Description_does_not_leak_password()
    {
        var settings = DatabaseConfig.Build(Config(("MYSQL_URL", "mysql://app:topsecret@db.example.com:3306/journeys")));
        Assert.Equal(DbProvider.MySql, settings.Provider);
        Assert.DoesNotContain("topsecret", settings.Description);
    }
}
