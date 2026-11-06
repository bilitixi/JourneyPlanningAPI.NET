namespace JourneyPlanningApi.Models;

public class ApiUsageLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Parameters { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }

    public object ToDict() => new Dictionary<string, object?>
    {
        ["id"] = Id,
        ["user_id"] = UserId,
        ["endpoint"] = Endpoint,
        ["method"] = Method,
        ["parameters"] = Parameters,
        ["status_code"] = StatusCode,
        ["created_at"] = Models.User.Iso(CreatedAt),
    };
}
