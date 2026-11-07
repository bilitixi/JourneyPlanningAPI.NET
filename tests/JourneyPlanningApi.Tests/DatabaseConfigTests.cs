using JourneyPlanningApi.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class DatabaseConfigTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Defaults_match_flask_db_py()
    {
        var cs = DatabaseConfig.Build(Config());
        Assert.Equal("localhost", cs.Server);
        Assert.Equal(3306u, cs.Port);
        Assert.Equal("root", cs.UserID);
        Assert.Equal("", cs.Password);
        Assert.Equal("journey_planning", cs.Database);
    }

    [Fact]
    public void Individual_variables_are_used()
    {
        var cs = DatabaseConfig.Build(Config(
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
        var cs = DatabaseConfig.Build(Config(
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
        var cs = DatabaseConfig.Build(Config(("DATABASE_URL", "mysql://app:pw@db.example.com/journeys")));
        Assert.Equal(3306u, cs.Port);
        Assert.Equal("journeys", cs.Database);
    }

    [Fact]
    public void Non_mysql_url_is_rejected_with_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfig.Build(Config(("DATABASE_URL", "postgres://u:p@host:5432/db"))));
        Assert.Contains("mysql://", ex.Message);
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
    public void Describe_does_not_leak_password()
    {
        var cs = DatabaseConfig.FromUrl("mysql://app:topsecret@db.example.com:3306/journeys");
        Assert.DoesNotContain("topsecret", DatabaseConfig.Describe(cs));
    }
}
