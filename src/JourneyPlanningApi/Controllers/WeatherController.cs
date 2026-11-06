using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Http;
using JourneyPlanningApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Controllers;

[ApiController]
[Route("api/weather")]
[TokenRequired]
public class WeatherController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;

    public WeatherController(AppDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    [HttpGet("{journeyId:int}")]
    public async Task<IActionResult> GetForecast(int journeyId)
    {
        try
        {
            var userId = CurrentUserId;
            var journey = await _db.Journeys.AsNoTracking()
                .FirstOrDefaultAsync(j => j.JourneyId == journeyId && j.UserId == userId);
            if (journey is null) return Error("Journey not found or access denied", 404);

            // Resolved lazily so a missing OPENWEATHER_API_KEY surfaces as a 500 for this
            // endpoint only (matching the Flask app), not at startup or for other routes.
            var weather = _services.GetRequiredService<WeatherService>();
            var r = await weather.GetWeatherForecastAsync(journey.Destination, journey.StartDate, journey.EndDate);
            return Respond(r.Body, r.Status);
        }
        catch (Exception e)
        {
            return Error(e.Message, 500);
        }
    }
}
