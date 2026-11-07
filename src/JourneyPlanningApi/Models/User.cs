namespace JourneyPlanningApi.Models;

public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "user";
    public bool IsVerified { get; set; }
    public string? VerificationToken { get; set; }
    public DateTime? VerificationExpires { get; set; }
    public string? ResetToken { get; set; }
    public DateTime? ResetExpires { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Journey> Journeys { get; set; } = new();

    public object ToDict() => new Dictionary<string, object?>
    {
        ["id"] = Id,
        ["first_name"] = FirstName,
        ["last_name"] = LastName,
        ["email"] = Email,
        ["role"] = Role,
        ["created_at"] = Iso(CreatedAt),
    };

    public static string? Iso(DateTime value) =>
        value == default ? null : value.ToString("yyyy-MM-dd'T'HH:mm:ss");
}
