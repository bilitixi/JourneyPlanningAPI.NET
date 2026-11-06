using System.Globalization;
using System.Text.Json.Nodes;
using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Http;
using JourneyPlanningApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Controllers;

[ApiController]
[Route("api/journeys")]
[TokenRequired]
public class JourneyController : ApiControllerBase
{
    private readonly AppDbContext _db;
    public JourneyController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = CurrentUserId;
        var journeys = await _db.Journeys.AsNoTracking().Where(j => j.UserId == userId).ToListAsync();
        return Respond(new { journeys = journeys.Select(j => j.ToDict()) }, 200);
    }

    [HttpGet("{journeyId:int}")]
    public async Task<IActionResult> Get(int journeyId)
    {
        var journey = await Find(journeyId);
        return journey is null ? Error("Journey not found", 404) : Respond(journey.ToDict(), 200);
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var data = await ReadJsonAsync();
        var missing = FirstMissing(data, "destination", "start_date", "end_date", "budget", "people");
        if (missing is not null) return Error($"Missing required field: {missing}", 400);

        if (!TryDate(data!, "start_date", out var start) || !TryDate(data!, "end_date", out var end))
            return Error("Invalid date format", 400);
        if (!TryDecimal(data!, "budget", out var budget)) return Error("Invalid budget", 400);
        if (!TryInt(data!, "people", out var people)) return Error("Invalid people", 400);
        var destination = GetString(data!, "destination");
        if (destination is null) return Error("Invalid destination", 400);

        var journey = new Journey
        {
            UserId = CurrentUserId ?? 0,
            Destination = destination,
            StartDate = start,
            EndDate = end,
            Budget = budget,
            People = people,
            Notes = GetString(data!, "notes"),
        };
        _db.Journeys.Add(journey);
        await _db.SaveChangesAsync();
        return Respond(journey.ToDict(), 201);
    }

    [HttpPut("{journeyId:int}")]
    public async Task<IActionResult> Update(int journeyId)
    {
        var journey = await Find(journeyId, track: true);
        if (journey is null) return Error("Journey not found", 404);

        var data = await ReadJsonAsync() ?? new();

        if (data.ContainsKey("destination"))
        {
            var d = GetString(data, "destination");
            if (d is null) return Error("Invalid destination", 400);
            journey.Destination = d;
        }
        if (data.ContainsKey("start_date"))
        {
            if (!TryDate(data, "start_date", out var s)) return Error("Invalid date format", 400);
            journey.StartDate = s;
        }
        if (data.ContainsKey("end_date"))
        {
            if (!TryDate(data, "end_date", out var e)) return Error("Invalid date format", 400);
            journey.EndDate = e;
        }
        if (data.ContainsKey("budget"))
        {
            if (!TryDecimal(data, "budget", out var b)) return Error("Invalid budget", 400);
            journey.Budget = b;
        }
        if (data.ContainsKey("people"))
        {
            if (!TryInt(data, "people", out var p)) return Error("Invalid people", 400);
            journey.People = p;
        }
        // Note: the Flask implementation silently ignores "notes" on update; here it is honoured.
        if (data.ContainsKey("notes")) journey.Notes = GetString(data, "notes");

        await _db.SaveChangesAsync();
        return Respond(journey.ToDict(), 200);
    }

    [HttpDelete("{journeyId:int}")]
    public async Task<IActionResult> Delete(int journeyId)
    {
        var journey = await Find(journeyId, track: true);
        if (journey is null) return Error("Journey not found", 404);

        _db.Journeys.Remove(journey);
        await _db.SaveChangesAsync();
        return Respond(new { message = "deleted successfully" }, 200);
    }

    private async Task<Journey?> Find(int journeyId, bool track = false)
    {
        var userId = CurrentUserId;
        var q = track ? _db.Journeys : _db.Journeys.AsNoTracking();
        return await q.FirstOrDefaultAsync(j => j.JourneyId == journeyId && j.UserId == userId);
    }

    private static bool TryDate(JsonObject o, string key, out DateOnly date)
    {
        date = default;
        var s = o[key] is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
        return s is not null &&
               DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryDecimal(JsonObject o, string key, out decimal value)
    {
        value = 0;
        if (o[key] is not JsonValue v) return false;
        if (v.TryGetValue<decimal>(out value)) return true;
        return v.TryGetValue<string>(out var s) &&
               decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryInt(JsonObject o, string key, out int value)
    {
        value = 0;
        if (o[key] is not JsonValue v) return false;
        if (v.TryGetValue<int>(out value)) return true;
        return v.TryGetValue<string>(out var s) &&
               int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
