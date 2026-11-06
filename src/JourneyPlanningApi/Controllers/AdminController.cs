using JourneyPlanningApi.Auth;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JourneyPlanningApi.Controllers;

[ApiController]
[Route("api/admin")]
[AdminRequired]
public class AdminController : ApiControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    /// <summary>Admin endpoint to retrieve API usage logs, newest first.</summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs()
    {
        try
        {
            var logs = (await _db.ApiUsageLogs.AsNoTracking().ToListAsync())
                .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
                .Select(l => l.ToDict()).ToList();
            return Respond(new { count = logs.Count, logs }, 200);
        }
        catch (Exception e)
        {
            return Error(e.Message, 500);
        }
    }
}
