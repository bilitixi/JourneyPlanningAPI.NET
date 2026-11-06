namespace JourneyPlanningApi.Models;

public class Journey
{
    public int JourneyId { get; set; }
    public int UserId { get; set; }
    public string Destination { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Budget { get; set; }
    public int People { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }

    public object ToDict() => new Dictionary<string, object?>
    {
        ["journey_id"] = JourneyId,
        ["destination"] = Destination,
        ["start_date"] = StartDate.ToString("yyyy-MM-dd"),
        ["end_date"] = EndDate.ToString("yyyy-MM-dd"),
        ["budget"] = Budget,
        ["people"] = People,
        ["notes"] = Notes,
    };
}
