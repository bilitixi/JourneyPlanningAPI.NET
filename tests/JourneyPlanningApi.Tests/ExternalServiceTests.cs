using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class ExternalServiceTests
{
    private static HttpResponseMessage JsonResp(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<int> CreateJourney(HttpClient c, string dest = "Wellington") =>
        (int)(await TestApp.Json(await c.PostAsJsonAsync("/api/journeys", new
        {
            destination = dest, start_date = "2026-12-01", end_date = "2026-12-02", budget = 1000, people = 2,
        })))["journey_id"]!;

    // 2026-12-01 00:00 / 03:00 UTC and 2026-12-02 00:00 UTC, plus one out-of-range item
    private const string Forecast = """
    {"list":[
      {"dt":1796083200,"main":{"temp":20.4},"weather":[{"description":"light rain"}],"pop":0.3},
      {"dt":1796094000,"main":{"temp":21.0},"weather":[{"description":"light rain"}],"pop":0.8},
      {"dt":1796169600,"main":{"temp":10.5},"weather":[{"description":"clear sky"}]},
      {"dt":1797000000,"main":{"temp":99},"weather":[{"description":"snow"}],"pop":1}
    ]}
    """;

    [Fact]
    public async Task Weather_returns_daily_summaries()
    {
        using var app = new TestApp();
        app.External.Respond = req => req.RequestUri!.AbsoluteUri.Contains("geo/1.0/direct")
            ? JsonResp("""[{"lat":-41.29,"lon":174.78}]""")
            : JsonResp(Forecast);
        var (c, _) = await app.RegisterAndLogin();
        var id = await CreateJourney(c);

        var r = await c.GetAsync($"/api/weather/{id}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await TestApp.Json(r);
        Assert.Equal("Wellington", (string?)j["destination"]);
        var f = j["forecast"]!.AsArray();
        Assert.Equal(2, f.Count);
        Assert.Equal("2026-12-01", (string?)f[0]!["date"]);
        Assert.Equal(21, (int?)f[0]!["temperature"]);   // mean 20.7 -> 21
        Assert.Equal("Light rain", (string?)f[0]!["condition"]);
        Assert.Equal(80, (int?)f[0]!["rain_probability"]);
        Assert.Equal("Clear sky", (string?)f[1]!["condition"]);
        Assert.Equal(0, (int?)f[1]!["rain_probability"]);
    }

    [Fact]
    public async Task Weather_unknown_location_is_404_and_foreign_journey_is_404()
    {
        using var app = new TestApp();
        app.External.Respond = _ => JsonResp("[]");
        var (c, _) = await app.RegisterAndLogin("a@x.com");
        var (other, _) = await app.RegisterAndLogin("b@x.com");
        var id = await CreateJourney(c, "Nowhereville");

        var r = await c.GetAsync($"/api/weather/{id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Location not found", (string?)(await TestApp.Json(r))["error"]);

        var denied = await other.GetAsync($"/api/weather/{id}");
        Assert.Equal("Journey not found or access denied", (string?)(await TestApp.Json(denied))["error"]);
    }

    [Fact]
    public async Task Weather_upstream_failure_is_500()
    {
        using var app = new TestApp();
        app.External.Respond = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);
        var (c, _) = await app.RegisterAndLogin();
        var id = await CreateJourney(c);
        var r = await c.GetAsync($"/api/weather/{id}");
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.StartsWith("Weather API error:", (string?)(await TestApp.Json(r))["error"]);
    }

    [Fact]
    public async Task Weather_without_api_key_is_500()
    {
        using var app = new TestApp(weatherKey: null);
        var (c, _) = await app.RegisterAndLogin();
        var id = await CreateJourney(c);
        var r = await c.GetAsync($"/api/weather/{id}");
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("OPENWEATHER_API_KEY not found", (string?)(await TestApp.Json(r))["error"]);
    }

    [Fact]
    public async Task Recommendations_parse_and_clean_model_output()
    {
        using var app = new TestApp();
        string? sentBody = null;
        app.External.Respond = req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            return JsonResp("""
            {"choices":[{"message":{"content":"1. Te Papa\n- Cable Car\n\n* Zealandia\n4) Weta Workshop\n5. A\n6. B\n7. C"}}]}
            """);
        };
        var (c, _) = await app.RegisterAndLogin();
        var id = await CreateJourney(c);

        var r = await c.PostAsync($"/api/recommendations/{id}", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await TestApp.Json(r);
        Assert.Equal("Wellington", (string?)j["destination"]);
        var recs = j["recommendations"]!.AsArray().Select(x => (string?)x).ToList();
        Assert.Equal(new[] { "Te Papa", "Cable Car", "Zealandia", ") Weta Workshop", "A", "B" }, recs);
        Assert.Contains("Duration: 2 days (2026-12-01 to 2026-12-02)", sentBody);
        Assert.Contains("nvidia/nemotron-3-super-120b-a12b:free", sentBody);
    }

    [Fact]
    public async Task Recommendations_fall_back_when_api_fails()
    {
        using var app = new TestApp();
        app.External.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (c, _) = await app.RegisterAndLogin();
        var known = await CreateJourney(c, "Queenstown");
        var unknown = await CreateJourney(c, "Paris");

        var k = await TestApp.Json(await c.PostAsync($"/api/recommendations/{known}", null));
        Assert.Equal("Skyline Gondola", (string?)k["recommendations"]![0]);

        var u = await TestApp.Json(await c.PostAsync($"/api/recommendations/{unknown}", null));
        Assert.Equal("Explore Paris city center", (string?)u["recommendations"]![0]);
        Assert.Equal(4, u["recommendations"]!.AsArray().Count);
    }

    [Fact]
    public async Task Recommendations_404_for_missing_journey()
    {
        using var app = new TestApp();
        var (c, _) = await app.RegisterAndLogin();
        var r = await c.PostAsync("/api/recommendations/9999", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Journey not found", (string?)(await TestApp.Json(r))["error"]);
    }
}
