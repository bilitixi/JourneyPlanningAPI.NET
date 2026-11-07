namespace JourneyPlanningApi.Http;

/// <summary>
/// Origins allowed to call /api/* from a browser. Override with the comma-separated <c>CORS_ORIGINS</c>
/// variable. Entries are exact origins (scheme + host [+ port]), <c>localhost</c> (http/https on localhost or
/// 127.0.0.1, any port) or <c>*</c> (any origin).
/// </summary>
public sealed class CorsOrigins
{
    public const string Default =
        "https://journeyplanningapp.bilitixi.org,https://journeyplanninguidraft.vercel.app,localhost";

    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _localhost;
    private readonly bool _any;

    public CorsOrigins(string? configured)
    {
        var list = string.IsNullOrWhiteSpace(configured) ? Default : configured;
        foreach (var entry in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry == "*") _any = true;
            else if (entry.Equals("localhost", StringComparison.OrdinalIgnoreCase)) _localhost = true;
            else _exact.Add(entry.TrimEnd('/'));
        }
    }

    public bool IsAllowed(string origin)
    {
        if (_any || _exact.Contains(origin)) return true;
        return _localhost
            && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host is "localhost" or "127.0.0.1" or "[::1]";
    }
}
