namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Promotion Suggestion Response DTO from AI Service
/// </summary>
public class PromotionSuggestionResponseDto
{
    public Guid BranchId { get; set; }
    public List<PromotionSuggestionItemDto> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Individual promotion suggestion item
/// </summary>
public class PromotionSuggestionItemDto
{
    public string TimeSlot { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;
    public double CurrentOccupancyPercent { get; set; }
    public int DiscountPercent { get; set; } // 10 to 50
    public string TargetSegment { get; set; } = string.Empty; // "Students", "Seniors", "All"
    public int SuggestedDurationDays { get; set; }
    public decimal EstimatedRevenueImpact { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
