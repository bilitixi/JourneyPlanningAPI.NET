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
