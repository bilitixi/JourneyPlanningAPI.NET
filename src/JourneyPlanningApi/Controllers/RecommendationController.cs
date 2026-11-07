using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Http;
using JourneyPlanningApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Controllers;

[ApiController]
[Route("api/recommendations")]
[TokenRequired]
public class RecommendationController : ApiControllerBase
{
    private readonly AppDbContext _db;
    private readonly AiService _ai;

    public RecommendationController(AppDbContext db, AiService ai) { _db = db; _ai = ai; }

    [HttpPost("{journeyId:int}")]
    public async Task<IActionResult> Generate(int journeyId)
    {
        try
        {
            // Only the journey's owner may ask for recommendations; someone else's journey gets the same 404 as a
            // missing one so ids can't be probed. (The Flask app looked journeys up by id only.)
            var userId = CurrentUserId;
            var journey = await _db.Journeys.AsNoTracking()
                .FirstOrDefaultAsync(j => j.JourneyId == journeyId && j.UserId == userId);
            if (journey is null) return Error("Journey not found", 404);

            var result = await _ai.GenerateRecommendationsAsync(
                journey.Destination,
                journey.StartDate.ToString("yyyy-MM-dd"),
                journey.EndDate.ToString("yyyy-MM-dd"),
                (double)journey.Budget,
                journey.People);
            return Respond(result, 200);
        }
        catch (Exception e)
        {
            return Respond(new { error = "Failed to generate recommendations", details = e.Message }, 500);
        }
    }
}
