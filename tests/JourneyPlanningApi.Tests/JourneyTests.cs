using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class JourneyTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static object Sample(string dest = "Queenstown") => new
    {
        destination = dest, start_date = "2026-12-01", end_date = "2026-12-07",
        budget = 2500, people = 2, notes = "Ski trip",
    };

    [Fact]
    public async Task Create_returns_201_with_journey_id_and_fields()
    {
        var (c, _) = await _app.RegisterAndLogin();
        var r = await c.PostAsJsonAsync("/api/journeys", Sample());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await TestApp.Json(r);
        Assert.True((int?)j["journey_id"] > 0);
        Assert.Equal("Queenstown", (string?)j["destination"]);
        Assert.Equal("2026-12-01", (string?)j["start_date"]);
        Assert.Equal("2026-12-07", (string?)j["end_date"]);
        Assert.Equal(2500m, (decimal?)j["budget"]);
        Assert.Equal(2, (int?)j["people"]);
        Assert.Equal("Ski trip", (string?)j["notes"]);
    }

    [Fact]
    public async Task Create_validates_required_fields_and_dates()
    {
        var (c, _) = await _app.RegisterAndLogin();
        var missing = await c.PostAsJsonAsync("/api/journeys", new { destination = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Missing required field: start_date", (string?)(await TestApp.Json(missing))["error"]);

        var bad = await c.PostAsJsonAsync("/api/journeys", new
            { destination = "X", start_date = "01/12/2026", end_date = "2026-12-07", budget = 1, people = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("Invalid date format", (string?)(await TestApp.Json(bad))["error"]);
    }

    [Fact]
    public async Task List_and_get_are_scoped_to_the_owner()
    {
        var (alice, _) = await _app.RegisterAndLogin("alice@x.com");
        var (bob, _) = await _app.RegisterAndLogin("bob@x.com");
        var id = (int)(await TestApp.Json(await alice.PostAsJsonAsync("/api/journeys", Sample())))["journey_id"]!;

        var aliceList = await TestApp.Json(await alice.GetAsync("/api/journeys"));
        Assert.Single(aliceList["journeys"]!.AsArray());

        var bobList = await TestApp.Json(await bob.GetAsync("/api/journeys"));
        Assert.Empty(bobList["journeys"]!.AsArray());

        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/journeys/{id}")).StatusCode);
        var denied = await bob.GetAsync($"/api/journeys/{id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("Journey not found", (string?)(await TestApp.Json(denied))["error"]);
    }

    [Fact]
    public async Task Update_changes_only_supplied_fields_and_enforces_ownership()
    {
        var (alice, _) = await _app.RegisterAndLogin("alice@x.com");
        var (bob, _) = await _app.RegisterAndLogin("bob@x.com");
        var id = (int)(await TestApp.Json(await alice.PostAsJsonAsync("/api/journeys", Sample())))["journey_id"]!;

        var r = await alice.PutAsJsonAsync($"/api/journeys/{id}", new { destination = "Auckland", people = 4 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await TestApp.Json(r);
        Assert.Equal("Auckland", (string?)j["destination"]);
        Assert.Equal(4, (int?)j["people"]);
        Assert.Equal("2026-12-01", (string?)j["start_date"]);

        var bad = await alice.PutAsJsonAsync($"/api/journeys/{id}", new { end_date = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var denied = await bob.PutAsJsonAsync($"/api/journeys/{id}", new { destination = "Hacked" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_journey()
    {
        var (c, _) = await _app.RegisterAndLogin();
        var id = (int)(await TestApp.Json(await c.PostAsJsonAsync("/api/journeys", Sample())))["journey_id"]!;

        var del = await c.DeleteAsync($"/api/journeys/{id}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal("deleted successfully", (string?)(await TestApp.Json(del))["message"]);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/journeys/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/journeys/{id}")).StatusCode);
    }
}
