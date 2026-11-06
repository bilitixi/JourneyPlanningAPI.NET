using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JourneyPlanningApi.Auth;

public record JwtPayload(int? UserId, string? Email, string? Role);

public enum JwtFailure { None, Expired, Invalid }

/// <summary>
/// Minimal HS256 JWT implementation, wire-compatible with PyJWT tokens issued by the Flask API
/// (claims: user_id, email, role, exp, iat). Written by hand because the default secret
/// ("test-secret-key", 15 bytes) is below the key-size minimum enforced by Microsoft.IdentityModel.
/// </summary>
public class JwtService
{
    private readonly byte[] _key;

    public JwtService(IConfiguration config)
    {
        var secret = config["JWT_SECRET_KEY"];
        _key = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(secret) ? "test-secret-key" : secret);
    }

    public string CreateToken(int userId, string email, string role, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new JsonObject { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new JsonObject
        {
            ["user_id"] = userId,
            ["email"] = email,
            ["role"] = role,
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
        };
        var signingInput = B64(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." +
                           B64(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return signingInput + "." + B64(Sign(signingInput));
    }

    public (JwtFailure Failure, JwtPayload? Payload) Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return (JwtFailure.Invalid, null);

        try
        {
            var header = JsonNode.Parse(Encoding.UTF8.GetString(FromB64(parts[0])))?.AsObject();
            if (header?["alg"]?.GetValue<string>() != "HS256") return (JwtFailure.Invalid, null);

            var expected = Sign(parts[0] + "." + parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, FromB64(parts[2])))
                return (JwtFailure.Invalid, null);

            var payload = JsonNode.Parse(Encoding.UTF8.GetString(FromB64(parts[1])))?.AsObject();
            if (payload is null) return (JwtFailure.Invalid, null);

            if (payload["exp"] is JsonValue exp && exp.TryGetValue<long>(out var expSeconds) &&
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expSeconds)
                return (JwtFailure.Expired, null);

            int? userId = payload["user_id"] is JsonValue u && u.TryGetValue<int>(out var id) ? id : null;
            return (JwtFailure.None, new JwtPayload(
                userId,
                payload["email"] is JsonValue e && e.TryGetValue<string>(out var email) ? email : null,
                payload["role"] is JsonValue r && r.TryGetValue<string>(out var role) ? role : null));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return (JwtFailure.Invalid, null);
        }
    }

    private byte[] Sign(string input) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(input));

    private static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
