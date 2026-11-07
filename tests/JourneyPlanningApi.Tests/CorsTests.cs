using JourneyPlanningApi.Http;
using Xunit;

namespace JourneyPlanningApi.Tests;

public class CorsTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return await _app.CreateClient().SendAsync(req);
    }

    [Theory]
    [InlineData("https://journeyplanningapp.bilitixi.org")]
    [InlineData("https://journeyplanninguidraft.vercel.app")]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:8080")]
    public async Task Allowed_origins_get_cors_headers(string origin)
    {
        var r = await Preflight(origin);
        Assert.True(r.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(origin, Assert.Single(values));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://journeyplanningapp.bilitixi.org")] // plain http, not https
    [InlineData("https://journeyplanningapp.bilitixi.org.evil.com")]
    [InlineData("https://localhost.evil.com")]
    public async Task Other_origins_get_no_cors_headers(string origin)
    {
        var r = await Preflight(origin);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Configured_list_replaces_default()
    {
        var cors = new CorsOrigins("https://a.example.com/, https://b.example.com");
        Assert.True(cors.IsAllowed("https://a.example.com"));
        Assert.True(cors.IsAllowed("https://b.example.com"));
        Assert.False(cors.IsAllowed("https://journeyplanningapp.bilitixi.org"));
        Assert.False(cors.IsAllowed("http://localhost:3000"));
    }

    [Fact]
    public void Star_allows_any_origin()
    {
        Assert.True(new CorsOrigins("*").IsAllowed("https://anything.example.com"));
    }
}
