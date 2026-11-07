using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Http;
using JourneyPlanningApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace JourneyPlanningApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("register")]
    public async Task<IActionResult> Register()
    {
        var data = await ReadJsonAsync();
        var missing = FirstMissing(data, "first_name", "last_name", "email", "password");
        if (missing is not null) return Error($"Missing required field: {missing}", 400);

        var r = await _auth.RegisterAsync(data!);
        return Respond(r.Body, r.Status);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        var data = await ReadJsonAsync();
        var missing = FirstMissing(data, "email", "password");
        if (missing is not null) return Error($"Missing required field: {missing}", 400);

        var r = await _auth.LoginAsync(data!);
        return Respond(r.Body, r.Status);
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail()
    {
        var data = await ReadJsonAsync();
        if (FirstMissing(data, "token") is { } missing) return Error($"Missing required field: {missing}", 400);
        var r = await _auth.VerifyEmailAsync(GetString(data!, "token"));
        return Respond(r.Body, r.Status);
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification()
    {
        var data = await ReadJsonAsync();
        if (FirstMissing(data, "email") is { } missing) return Error($"Missing required field: {missing}", 400);
        var r = await _auth.ResendVerificationEmailAsync(GetString(data!, "email"));
        return Respond(r.Body, r.Status);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword()
    {
        var data = await ReadJsonAsync();
        if (FirstMissing(data, "email") is { } missing) return Error($"Missing required field: {missing}", 400);
        var r = await _auth.ForgotPasswordAsync(GetString(data!, "email"));
        return Respond(r.Body, r.Status);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword()
    {
        var data = await ReadJsonAsync();
        if (FirstMissing(data, "token", "new_password") is { } missing)
            return Error($"Missing required field: {missing}", 400);
        var r = await _auth.ResetPasswordAsync(GetString(data!, "token"), GetString(data!, "new_password"));
        return Respond(r.Body, r.Status);
    }

    [HttpPut("me"), TokenRequired]
    public async Task<IActionResult> UpdateMe()
    {
        var data = await ReadJsonAsync() ?? new();
        var r = await _auth.UpdateUserAsync(CurrentUserId, data);
        return Respond(r.Body, r.Status);
    }

    [HttpDelete("me"), TokenRequired]
    public async Task<IActionResult> DeleteMe()
    {
        var data = await ReadJsonAsync() ?? new();
        var r = await _auth.DeleteUserAsync(CurrentUserId, data);
        return Respond(r.Body, r.Status);
    }
}
