using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace JourneyPlanningApi.Services;

public class AiService
{
    public const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    public const string Model = "nvidia/nemotron-3-super-120b-a12b:free";

    private static readonly char[] LeadingJunk = "0123456789.-* ".ToCharArray();

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public AiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["OPENROUTER_API_KEY"];
    }

    /// <summary>Generates recommendations via OpenRouter; falls back to canned ones on any failure.</summary>
    public async Task<object> GenerateRecommendationsAsync(
        string destination, string startDate, string endDate, double budget, int people)
    {
        try
        {
            var start = DateOnly.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateOnly.ParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var duration = end.DayNumber - start.DayNumber + 1;

            var prompt = $"""
                Generate 4-6 travel recommendations for {destination}.

                Trip details:
                - Destination: {destination}
                - Duration: {duration} days ({startDate} to {endDate})
                - Budget: ${budget.ToString(CultureInfo.InvariantCulture)}
                - Number of travelers: {people}

                Provide specific attractions, activities, or experiences that are suitable for this destination and budget.
                Return only the recommendations as a comma-separated list, with each recommendation on a new line.
                """;

            var body = new JsonObject
            {
                ["model"] = Model,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = "You are a travel recommendation assistant. Provide specific, practical travel recommendations.",
                    },
                    new JsonObject { ["role"] = "user", ["content"] = prompt },
                },
                ["reasoning"] = new JsonObject { ["enabled"] = true },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            var text = json!["choices"]![0]!["message"]!["content"]!.GetValue<string>().Trim();

            var recommendations = text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => l.TrimStart(LeadingJunk))
                .Where(l => l.Length > 0)
                .Take(6)
                .ToList();

            return new Dictionary<string, object?>
            {
                ["destination"] = destination,
                ["recommendations"] = recommendations,
            };
        }
        catch (Exception)
        {
            return GetFallbackRecommendations(destination);
        }
    }

    private static readonly Dictionary<string, string[]> Fallbacks = new()
    {
        ["Queenstown"] = new[] { "Skyline Gondola", "Milford Sound Tour", "Lake Wakatipu", "Ben Lomond Track" },
        ["Auckland"] = new[] { "Sky Tower", "Auckland War Memorial Museum", "Viaduct Harbour", "Mount Eden" },
        ["Wellington"] = new[] { "Te Papa Museum", "Cable Car", "Zealandia", "Weta Workshop" },
        ["Christchurch"] = new[] { "Botanic Gardens", "Canterbury Museum", "International Antarctic Centre", "Cashel Street" },
    };

    public static object GetFallbackRecommendations(string destination)
    {
        var recs = Fallbacks.TryGetValue(destination, out var known)
            ? known
            : new[]
            {
                $"Explore {destination} city center",
                "Visit local museums",
                "Try local cuisine",
                "Walk through parks",
            };

        return new Dictionary<string, object?> { ["destination"] = destination, ["recommendations"] = recs };
    }
}
