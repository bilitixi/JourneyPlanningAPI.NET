using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JourneyPlanningApi.Services;

public class WeatherService
{
    public const string ForecastUrl = "https://api.openweathermap.org/data/2.5/forecast";
    public const string GeocodingUrl = "https://api.openweathermap.org/geo/1.0/direct";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public WeatherService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        var key = config["OPENWEATHER_API_KEY"];
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("OPENWEATHER_API_KEY not found");
        _apiKey = key;
    }

    public async Task<ServiceResult> GetWeatherForecastAsync(string destination, DateOnly startDate, DateOnly endDate)
    {
        try
        {
            if (startDate > endDate)
                return new(new { error = "start_date must be before end_date" }, 400);

            // Geocode the destination
            var geoUrl = $"{GeocodingUrl}?q={Uri.EscapeDataString(destination)}&limit=1&appid={Uri.EscapeDataString(_apiKey)}";
            using var geoResp = await _http.GetAsync(geoUrl);
            geoResp.EnsureSuccessStatusCode();
            var locations = JsonNode.Parse(await geoResp.Content.ReadAsStringAsync())?.AsArray();
            if (locations is null || locations.Count == 0)
                return new(new { error = "Location not found" }, 404);

            var lat = locations[0]!["lat"]!.GetValue<double>();
            var lon = locations[0]!["lon"]!.GetValue<double>();

            // Fetch the forecast
            var fcUrl = string.Create(CultureInfo.InvariantCulture,
                $"{ForecastUrl}?lat={lat}&lon={lon}&appid={Uri.EscapeDataString(_apiKey)}&units=metric");
            using var fcResp = await _http.GetAsync(fcUrl);
            fcResp.EnsureSuccessStatusCode();
            var data = JsonNode.Parse(await fcResp.Content.ReadAsStringAsync())?.AsObject();

            // Group by day (UTC)
            var daily = new SortedDictionary<DateOnly, (List<double> Temps, List<string> Conditions, List<int> Rain)>();
            foreach (var item in data?["list"]?.AsArray() ?? new JsonArray())
            {
                var date = DateOnly.FromDateTime(
                    DateTimeOffset.FromUnixTimeSeconds(item!["dt"]!.GetValue<long>()).UtcDateTime);
                if (date < startDate || date > endDate) continue;

                if (!daily.TryGetValue(date, out var day))
                    daily[date] = day = (new(), new(), new());

                day.Temps.Add(item["main"]!["temp"]!.GetValue<double>());
                day.Conditions.Add(item["weather"]![0]!["description"]!.GetValue<string>());
                var pop = item["pop"] is JsonValue p && p.TryGetValue<double>(out var pv) ? pv : 0;
                day.Rain.Add((int)(pop * 100));
            }

            var forecast = new List<object>();
            foreach (var (date, day) in daily)
            {
                // Most common condition; ties go to the first one seen (same as Counter.most_common)
                var condition = day.Conditions
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => day.Conditions.IndexOf(g.Key))
                    .First().Key;

                forecast.Add(new Dictionary<string, object?>
                {
                    ["date"] = date.ToString("yyyy-MM-dd"),
                    ["temperature"] = (int)Math.Round(day.Temps.Average(), MidpointRounding.ToEven),
                    ["condition"] = Capitalize(condition),
                    ["rain_probability"] = day.Rain.Max(),
                });
            }

            if (forecast.Count == 0)
                return new(new { error = "No forecast data available for the selected dates" }, 404);

            return new(new Dictionary<string, object?> { ["destination"] = destination, ["forecast"] = forecast }, 200);
        }
        catch (TaskCanceledException)
        {
            return new(new { error = "Weather service timed out" }, 504);
        }
        catch (HttpRequestException e)
        {
            return new(new { error = $"Weather API error: {e.Message}" }, 500);
        }
        catch (Exception e)
        {
            return new(new { error = e.Message }, 500);
        }
    }

    // Python str.capitalize(): first character upper-case, the rest lower-case.
    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
