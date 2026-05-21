namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Pricing Suggestion Response DTO from AI Service
/// </summary>
public class PricingSuggestionResponseDto
{
    public List<PricingSuggestionItemDto> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Individual pricing suggestion item
/// </summary>
public class PricingSuggestionItemDto
{
    public string TimeSlot { get; set; } = string.Empty; // "06:00-08:00"
    public string DayOfWeek { get; set; } = string.Empty; // "Monday" or "Weekday"
    public decimal CurrentPrice { get; set; }
    public decimal SuggestedIncreasePercent { get; set; } // -20 to +30
    public decimal SuggestedPrice { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public double Confidence { get; set; } // 0.0 to 1.0
}
