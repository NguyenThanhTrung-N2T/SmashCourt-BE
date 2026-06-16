namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Booking Suggestion Response DTO from AI Service
/// </summary>
public class BookingSuggestionResponseDto
{
    public List<BookingSuggestionItemDto> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Individual booking suggestion item
/// </summary>
public class BookingSuggestionItemDto
{
    public string Type { get; set; } = string.Empty; // "time_slot", "branch", "court_type"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}
