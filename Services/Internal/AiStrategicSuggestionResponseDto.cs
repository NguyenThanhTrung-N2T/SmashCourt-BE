namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw strategic suggestion response from AI Service.
/// This is validated and transformed into StrategicSuggestionResponseDto by AIResponseFormatterService.
/// Strategic suggestions are for OWNER role only and include cross-branch analysis.
/// </summary>
public class AiStrategicSuggestionResponseDto
{
    public string Period { get; set; } = string.Empty;
    public List<AiStrategicInsightDto> Insights { get; set; } = new();
    public List<AiBranchPerformanceDto> BranchPerformances { get; set; } = new();
    public List<AiStaffingRecommendationDto> StaffingRecommendations { get; set; } = new();
    public AiDemandForecastDto? DemandForecast { get; set; }
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Internal DTO representing a strategic insight from AI Service.
/// </summary>
public class AiStrategicInsightDto
{
    public string Category { get; set; } = string.Empty;  // "expansion" | "staffing" | "performance" | "optimization"
    public string Severity { get; set; } = string.Empty;  // "info" | "warning" | "critical" | "positive"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}

/// <summary>
/// Internal DTO representing branch performance comparison from AI Service.
/// </summary>
public class AiBranchPerformanceDto
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal OccupancyRate { get; set; }
    public int TotalBookings { get; set; }
    public string PerformanceRating { get; set; } = string.Empty;  // "excellent" | "good" | "average" | "poor"
}

/// <summary>
/// Internal DTO representing staffing recommendation from AI Service.
/// </summary>
public class AiStaffingRecommendationDto
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;  // "increase" | "decrease" | "maintain"
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>
/// Internal DTO representing demand forecast from AI Service.
/// </summary>
public class AiDemandForecastDto
{
    public int ForecastDays { get; set; }
    public decimal ExpectedGrowthPercent { get; set; }
    public List<string> PeakDays { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}
