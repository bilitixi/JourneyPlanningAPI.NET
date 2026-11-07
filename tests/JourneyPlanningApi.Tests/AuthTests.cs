using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class AuthTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Root_returns_hello_world()
    {
        var r = await _app.CreateClient().GetAsync("/");
        Assert.Equal("Hello World!", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_succeeds_then_rejects_duplicate_email()
    {
        var c = _app.CreateClient();
        var body = new { first_name = "John", last_name = "Smith", email = "a@b.com", password = "Password123" };
        var r1 = await c.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var j = await TestApp.Json(r1);
        Assert.Equal("User registered successfully", (string?)j["message"]);
        Assert.True((int?)j["user_id"] > 0);

        var r2 = await c.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal("Email already registered", (string?)(await TestApp.Json(r2))["error"]);
    }

    [Fact]
    public async Task Register_reports_first_missing_field()
    {
        var r = await _app.CreateClient().PostAsJsonAsync("/api/auth/register", new { first_name = "x", email = "e" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Missing required field: last_name", (string?)(await TestApp.Json(r))["error"]);
    }

    [Fact]
    public async Task Login_returns_token_and_user_and_rejects_bad_credentials()
    {
        var (_, _) = await _app.RegisterAndLogin("l@x.com");
        var c = _app.CreateClient();

        var ok = await c.PostAsJsonAsync("/api/auth/login", new { email = "l@x.com", password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var j = await TestApp.Json(ok);
        Assert.False(string.IsNullOrEmpty((string?)j["access_token"]));
        Assert.Equal("l@x.com", (string?)j["user"]!["email"]);
        Assert.Equal("user", (string?)j["user"]!["role"]);

        var bad = await c.PostAsJsonAsync("/api/auth/login", new { email = "l@x.com", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal("Invalid email or password", (string?)(await TestApp.Json(bad))["error"]);

        var unknown = await c.PostAsJsonAsync("/api/auth/login", new { email = "who@x.com", password = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);

        var missing = await c.PostAsJsonAsync("/api/auth/login", new { email = "l@x.com" });
        Assert.Equal("Missing required field: password", (string?)(await TestApp.Json(missing))["error"]);
    }

    [Fact]
    public async Task Protected_routes_reject_missing_invalid_and_expired_tokens()
    {
        var c = _app.CreateClient();
        var none = await c.GetAsync("/api/journeys");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("Token is missing", (string?)(await TestApp.Json(none))["error"]);

        c.DefaultRequestHeaders.Add("Authorization", "Bearer not.a.token");
        var bad = await c.GetAsync("/api/journeys");
        Assert.Equal("Invalid token", (string?)(await TestApp.Json(bad))["error"]);

        var c2 = _app.CreateClient();
        c2.DefaultRequestHeaders.Add("Authorization", "Bearer " + MakeToken(DateTimeOffset.UtcNow.AddHours(-1)));
        var expired = await c2.GetAsync("/api/journeys");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("Token has expired", (string?)(await TestApp.Json(expired))["error"]);
    }

    [Fact]
    public async Task Token_signed_with_other_secret_is_rejected()
    {
        var c = _app.CreateClient();
        c.DefaultRequestHeaders.Add("Authorization", "Bearer " + MakeToken(DateTimeOffset.UtcNow.AddHours(1), "wrong-secret"));
        var r = await c.GetAsync("/api/journeys");
        Assert.Equal("Invalid token", (string?)(await TestApp.Json(r))["error"]);
    }

    [Fact]
    public async Task Admin_logs_require_admin_role()
    {
        var (user, _) = await _app.RegisterAndLogin();
        var forbidden = await user.GetAsync("/api/admin/logs");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("Admin access required", (string?)(await TestApp.Json(forbidden))["error"]);

        var admin = _app.CreateClient();
        admin.DefaultRequestHeaders.Add("Authorization", "Bearer " + MakeToken(DateTimeOffset.UtcNow.AddHours(1), role: "admin"));
        var ok = await admin.GetAsync("/api/admin/logs");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var j = await TestApp.Json(ok);
        Assert.Equal(0, (int?)j["count"]);
    }

    [Fact]
    public async Task UpdateMe_changes_names_without_password_but_requires_it_for_email_and_password()
    {
        var (c, _) = await _app.RegisterAndLogin();

        var names = await c.PutAsJsonAsync("/api/auth/me", new { first_name = "Jane" });
        Assert.Equal(HttpStatusCode.OK, names.StatusCode);
        Assert.Equal("Jane", (string?)(await TestApp.Json(names))["user"]!["first_name"]);

        var noPw = await c.PutAsJsonAsync("/api/auth/me", new { email = "new@x.com" });
        Assert.Equal(HttpStatusCode.Unauthorized, noPw.StatusCode);

        var wrongPw = await c.PutAsJsonAsync("/api/auth/me", new { email = "new@x.com", current_password = "bad" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPw.StatusCode);

        var ok = await c.PutAsJsonAsync("/api/auth/me",
            new { email = "new@x.com", password = "Another456", current_password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("new@x.com", (string?)(await TestApp.Json(ok))["user"]!["email"]);

        var login = await _app.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "new@x.com", password = "Another456" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_rejects_email_owned_by_someone_else()
    {
        await _app.RegisterAndLogin("other@x.com");
        var (c, _) = await _app.RegisterAndLogin("me@x.com");
        var r = await c.PutAsJsonAsync("/api/auth/me", new { email = "other@x.com", current_password = "Password123" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Email already registered", (string?)(await TestApp.Json(r))["error"]);
    }

    [Fact]
    public async Task DeleteMe_requires_password_and_removes_account_and_journeys()
    {
        var (c, _) = await _app.RegisterAndLogin("del@x.com");
        await c.PostAsJsonAsync("/api/journeys", new
        {
            destination = "Auckland", start_date = "2026-12-01", end_date = "2026-12-05", budget = 100, people = 1,
        });

        var wrong = await c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me")
            { Content = TestApp.Raw("""{"current_password":"bad"}""") });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var none = await c.DeleteAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);

        var ok = await c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/me")
            { Content = TestApp.Raw("""{"current_password":"Password123"}""") });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Account deleted successfully", (string?)(await TestApp.Json(ok))["message"]);

        var login = await _app.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "del@x.com", password = "Password123" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    private static string MakeToken(DateTimeOffset exp, string secret = "unit-test-secret", string role = "user")
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = B64(Encoding.UTF8.GetBytes(
            $$"""{"user_id":1,"email":"t@x.com","role":"{{role}}","exp":{{exp.ToUnixTimeSeconds()}},"iat":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}"""));
        var sig = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(header + "." + payload)));
        return $"{header}.{payload}.{sig}";
    }
}
