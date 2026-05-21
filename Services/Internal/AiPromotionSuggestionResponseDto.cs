namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw promotion suggestion response from AI Service.
/// This is validated and transformed into PromotionSuggestionResponseDto by AIResponseFormatterService.
/// Validation: discountPercent must be between 10% and 50%.
/// </summary>
public class AiPromotionSuggestionResponseDto
{
    public string BranchId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public List<AiPromotionInsightDto> Insights { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Internal DTO representing a single promotion insight from AI Service.
/// </summary>
public class AiPromotionInsightDto
{
    public string Category { get; set; } = string.Empty;  // "promotion" | "occupancy" | "revenue"
    public string Severity { get; set; } = string.Empty;  // "info" | "warning" | "critical" | "positive"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
    public decimal? DiscountPercent { get; set; }  // Must be between 10% and 50%
    public string? TargetSegment { get; set; }
    public decimal? EstimatedRevenueImpact { get; set; }
}
