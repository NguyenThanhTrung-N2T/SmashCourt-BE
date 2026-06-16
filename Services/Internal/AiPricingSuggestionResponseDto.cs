namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw pricing suggestion response from AI Service.
/// This is validated and transformed into PricingSuggestionResponseDto by AIResponseFormatterService.
/// Validation: suggestedIncreasePercent must be between -20% and +30%.
/// </summary>
public class AiPricingSuggestionResponseDto
{
    public string BranchId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public List<AiPricingInsightDto> Insights { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Internal DTO representing a single pricing insight from AI Service.
/// </summary>
public class AiPricingInsightDto
{
    public string Category { get; set; } = string.Empty;  // "revenue" | "occupancy" | "pricing"
    public string Severity { get; set; } = string.Empty;  // "info" | "warning" | "critical" | "positive"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
    public decimal? SuggestedIncreasePercent { get; set; }  // Must be between -20% and +30%
}
