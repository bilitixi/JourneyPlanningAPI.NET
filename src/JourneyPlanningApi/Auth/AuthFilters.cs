using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JourneyPlanningApi.Auth;

/// <summary>Equivalent of Flask's @token_required decorator.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class TokenRequiredAttribute : Attribute, IAuthorizationFilter
{
    public const string ItemKey = "jwt_payload";
    protected virtual bool AdminOnly => false;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        string? token = null;

        var header = http.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            var parts = header.Split(' ');
            token = parts.Length > 1 ? parts[1] : null;
        }

        if (string.IsNullOrEmpty(token))
        {
            context.Result = Error(401, "Token is missing");
            return;
        }

        var jwt = http.RequestServices.GetRequiredService<JwtService>();
        var (failure, payload) = jwt.Validate(token);
        if (failure == JwtFailure.Expired) { context.Result = Error(401, "Token has expired"); return; }
        if (failure != JwtFailure.None || payload is null) { context.Result = Error(401, "Invalid token"); return; }

        if (AdminOnly && payload.Role != "admin")
        {
            context.Result = Error(403, "Admin access required");
            return;
        }

        http.Items[ItemKey] = payload;
    }

    private static IActionResult Error(int status, string message) =>
        new ObjectResult(new { error = message }) { StatusCode = status };
}

/// <summary>Equivalent of Flask's @admin_required decorator.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminRequiredAttribute : TokenRequiredAttribute
{
    protected override bool AdminOnly => true;
}
