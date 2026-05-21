namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw analytics summary response from AI Service.
/// This is validated and transformed into AnalyticsSummaryResponseDto by AIResponseFormatterService.
/// </summary>
public class AiAnalyticsSummaryResponseDto
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public List<AiInsightItemDto> Insights { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Internal DTO representing a single insight item from AI Service.
/// </summary>
public class AiInsightItemDto
{
    public string Category { get; set; } = string.Empty;  // "revenue" | "occupancy" | "cancellation" | "promotion"
    public string Severity { get; set; } = string.Empty;  // "info" | "warning" | "critical" | "positive"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}
