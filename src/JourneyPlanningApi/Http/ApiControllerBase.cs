using System.Text.Json;
using System.Text.Json.Nodes;
using JourneyPlanningApi.Auth;
using Microsoft.AspNetCore.Mvc;

namespace JourneyPlanningApi.Http;

public abstract class ApiControllerBase : ControllerBase
{
    protected JwtPayload? Payload => HttpContext.Items[TokenRequiredAttribute.ItemKey] as JwtPayload;
    protected int? CurrentUserId => Payload?.UserId;

    protected IActionResult Respond(object body, int status) => new ObjectResult(body) { StatusCode = status };
    protected IActionResult Error(string message, int status) => Respond(new { error = message }, status);

    /// <summary>Reads the request body as a JSON object; returns null when absent or not an object.</summary>
    protected async Task<JsonObject?> ReadJsonAsync()
    {
        try
        {
            var node = await JsonNode.ParseAsync(Request.Body);
            return node as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string? GetString(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    protected static string? FirstMissing(JsonObject? data, params string[] required) =>
        required.FirstOrDefault(f => data is null || !data.ContainsKey(f));
}
