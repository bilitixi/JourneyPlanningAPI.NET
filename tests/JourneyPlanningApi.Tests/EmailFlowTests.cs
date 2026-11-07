using System.Net;
using System.Net.Http.Json;
using JourneyPlanningApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JourneyPlanningApi.Tests;

/// <summary>Email verification and password reset, matching the Flask deploy branch.</summary>
public class EmailFlowTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static object NewUser(string email) =>
        new { first_name = "John", last_name = "Smith", email, password = "Password123" };

    private Task<HttpResponseMessage> Post(string path, object body) =>
        _app.CreateClient().PostAsJsonAsync(path, body);

    private static async Task<string?> Error(HttpResponseMessage r) => (string?)(await TestApp.Json(r))["error"];
    private static async Task<string?> Message(HttpResponseMessage r) => (string?)(await TestApp.Json(r))["message"];

    /// <summary>Moves a user's verification / reset expiry into the past.</summary>
    private async Task Expire(string email)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.VerificationExpires = user.VerificationExpires?.AddHours(-1);
        user.ResetExpires = user.ResetExpires?.AddHours(-1);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Register_sends_verification_link_and_login_is_blocked_until_verified()
    {
        Assert.Equal(HttpStatusCode.Created, (await Post("/api/auth/register", NewUser("v@x.com"))).StatusCode);
        var mail = Assert.Single(_app.Email.Sent);
        Assert.Equal("v@x.com", mail.To);
        Assert.Equal("Verify Your Email", mail.Subject);
        Assert.Contains("http://localhost:3000/verify-email?token=", mail.Text);

        var blocked = await Post("/api/auth/login", new { email = "v@x.com", password = "Password123" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("Please verify your email", await Error(blocked));

        var token = _app.Email.LastToken("v@x.com", "verify-email");
        var verified = await Post("/api/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("Email verified successfully", await Message(verified));

        var login = await Post("/api/auth/login", new { email = "v@x.com", password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // The token is single use
        Assert.Equal("Invalid token", await Error(await Post("/api/auth/verify-email", new { token })));
    }

    [Fact]
    public async Task Wrong_password_still_gets_401_for_unverified_user()
    {
        await Post("/api/auth/register", NewUser("u@x.com"));
        var r = await Post("/api/auth/login", new { email = "u@x.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Register_reports_email_failure()
    {
        _app.Email.Fail = true;
        var r = await Post("/api/auth/register", NewUser("f@x.com"));
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("User created but email failed", await Error(r));

        // No email went out, so resending is allowed straight away instead of 429 for 15 minutes
        var failedResend = await Post("/api/auth/resend-verification", new { email = "f@x.com" });
        Assert.Equal("Email send failed", await Error(failedResend));
        _app.Email.Fail = false;
        var resent = await Post("/api/auth/resend-verification", new { email = "f@x.com" });
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var token = _app.Email.LastToken("f@x.com", "verify-email");
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/verify-email", new { token })).StatusCode);
    }

    [Fact]
    public async Task Verify_email_rejects_missing_unknown_and_expired_tokens()
    {
        var missing = await Post("/api/auth/verify-email", new { });
        Assert.Equal("Missing required field: token", await Error(missing));
        Assert.Equal("Invalid token", await Error(await Post("/api/auth/verify-email", new { token = "nope" })));

        await Post("/api/auth/register", NewUser("e@x.com"));
        await Expire("e@x.com");
        var expired = await Post("/api/auth/verify-email", new { token = _app.Email.LastToken("e@x.com", "verify-email") });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal("Token expired", await Error(expired));
    }

    [Fact]
    public async Task Resend_verification_waits_for_expiry_then_sends_new_link()
    {
        Assert.Equal("User not found", await Error(await Post("/api/auth/resend-verification", new { email = "no@x.com" })));

        await Post("/api/auth/register", NewUser("r@x.com"));
        var tooSoon = await Post("/api/auth/resend-verification", new { email = "r@x.com" });
        Assert.Equal((HttpStatusCode)429, tooSoon.StatusCode);
        Assert.Equal("Please wait before requesting another verification email", await Error(tooSoon));

        await Expire("r@x.com");
        var resent = await Post("/api/auth/resend-verification", new { email = "r@x.com" });
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        Assert.Equal("Verification email sent", await Message(resent));
        Assert.Equal(2, _app.Email.Sent.Count);

        var token = _app.Email.LastToken("r@x.com", "verify-email");
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/verify-email", new { token })).StatusCode);
        Assert.Equal("Already verified", await Error(await Post("/api/auth/resend-verification", new { email = "r@x.com" })));
    }

    [Fact]
    public async Task Forgot_and_reset_password_changes_the_password()
    {
        await _app.RegisterAndLogin("p@x.com");

        var unknown = await Post("/api/auth/forgot-password", new { email = "nobody@x.com" });
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal("If email exists, reset sent", await Message(unknown));

        var sent = await Post("/api/auth/forgot-password", new { email = "p@x.com" });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("Reset email sent", await Message(sent));
        Assert.Equal("Reset Your Password", _app.Email.Sent.Last().Subject);
        Assert.Contains("http://localhost:3000/reset-password?token=", _app.Email.Sent.Last().Text);

        var again = await Post("/api/auth/forgot-password", new { email = "p@x.com" });
        Assert.Equal((HttpStatusCode)429, again.StatusCode);
        Assert.Equal("Please wait before requesting another password reset email", await Error(again));

        var token = _app.Email.LastToken("p@x.com", "reset-password");
        var reset = await Post("/api/auth/reset-password", new { token, new_password = "BrandNew789" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("Password reset successful", await Message(reset));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Post("/api/auth/login", new { email = "p@x.com", password = "Password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Post("/api/auth/login", new { email = "p@x.com", password = "BrandNew789" })).StatusCode);

        // Single use
        var reuse = await Post("/api/auth/reset-password", new { token, new_password = "Other0000" });
        Assert.Equal("Invalid token", await Error(reuse));
    }

    [Fact]
    public async Task Reset_password_validates_input_and_expiry()
    {
        Assert.Equal("Missing required field: email", await Error(await Post("/api/auth/forgot-password", new { })));
        Assert.Equal("Missing required field: new_password",
            await Error(await Post("/api/auth/reset-password", new { token = "x" })));
        Assert.Equal("Invalid token",
            await Error(await Post("/api/auth/reset-password", new { token = "x", new_password = "Password1" })));

        await _app.RegisterAndLogin("x@x.com");
        await Post("/api/auth/forgot-password", new { email = "x@x.com" });
        await Expire("x@x.com");
        var expired = await Post("/api/auth/reset-password",
            new { token = _app.Email.LastToken("x@x.com", "reset-password"), new_password = "Password1" });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal("Token expired", await Error(expired));
    }

    [Fact]
    public async Task Forgot_password_reports_email_failure()
    {
        await _app.RegisterAndLogin("ff@x.com");
        _app.Email.Fail = true;
        var r = await Post("/api/auth/forgot-password", new { email = "ff@x.com" });
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("Reset email failed", await Error(r));

        // The failed attempt doesn't block a retry with 429
        _app.Email.Fail = false;
        var retry = await Post("/api/auth/forgot-password", new { email = "ff@x.com" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("Reset email sent", await Message(retry));
    }

    [Fact]
    public async Task Unknown_routes_return_json_error()
    {
        var r = await _app.CreateClient().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Not found", await Error(r));
    }
}
