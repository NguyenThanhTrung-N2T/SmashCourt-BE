namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw booking suggestion response from AI Service.
/// This is validated and transformed into BookingSuggestionResponseDto by AIResponseFormatterService.
/// </summary>
public class AiBookingSuggestionResponseDto
{
    public string UserId { get; set; } = string.Empty;
    public List<AiSuggestionItemDto> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Internal DTO representing a single suggestion item from AI Service.
/// </summary>
public class AiSuggestionItemDto
{
    public string Type { get; set; } = string.Empty;  // "time_slot" | "court_type" | "branch" | "promotion"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Action { get; set; }  // label for CTA button, null = info only
}
