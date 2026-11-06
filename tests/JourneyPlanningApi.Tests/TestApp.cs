using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using JourneyPlanningApi.Data;
using JourneyPlanningApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JourneyPlanningApi.Tests;

public class StubHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(Respond(request));
}

public class TestApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    public StubHandler External { get; } = new();

    public TestApp(string? weatherKey = "test-weather-key")
    {
        _conn.Open();
        Environment.SetEnvironmentVariable("SKIP_DB_INIT", "true");
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "unit-test-secret");
        Environment.SetEnvironmentVariable("OPENWEATHER_API_KEY", weatherKey);
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test-ai-key");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
            services.AddHttpClient<WeatherService>().ConfigurePrimaryHttpMessageHandler(() => External);
            services.AddHttpClient<AiService>().ConfigurePrimaryHttpMessageHandler(() => External);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }

    public async Task<(HttpClient Client, int UserId)> RegisterAndLogin(
        string email = "john@example.com", string password = "Password123")
    {
        var client = CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new { first_name = "John", last_name = "Smith", email, password });
        var userId = (await reg.Content.ReadFromJsonAsync<JsonObject>())!["user_id"]!.GetValue<int>();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    public static async Task<JsonObject> Json(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonObject>())!;

    public static StringContent Raw(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}

static class ServiceCollectionExt
{
    public static void RemoveAll<T>(this IServiceCollection s)
    {
        foreach (var d in s.Where(d => d.ServiceType == typeof(T)).ToList()) s.Remove(d);
    }
}
