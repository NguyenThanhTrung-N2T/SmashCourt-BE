namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Strategic Suggestion Response DTO from AI Service
/// </summary>
public class StrategicSuggestionResponseDto
{
    public List<BranchPerformanceDto> BranchPerformance { get; set; } = new();
    public List<StaffingSuggestionDto> StaffingSuggestions { get; set; } = new();
    public List<ExpansionOpportunityDto> ExpansionOpportunities { get; set; } = new();
    public DemandForecastDto? DemandForecast { get; set; }
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Branch performance comparison
/// </summary>
public class BranchPerformanceDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string PerformanceRating { get; set; } = string.Empty; // "Excellent", "Good", "Needs Improvement"
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
}

/// <summary>
/// Staffing suggestion for a branch
/// </summary>
public class StaffingSuggestionDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>
/// Expansion opportunity recommendation
/// </summary>
public class ExpansionOpportunityDto
{
    public string Location { get; set; } = string.Empty;
    public string Opportunity { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public double Priority { get; set; } // 0.0 to 1.0
}

/// <summary>
/// Demand forecast for next 30 days
/// </summary>
public class DemandForecastDto
{
    public string Summary { get; set; } = string.Empty;
    public List<string> Predictions { get; set; } = new();
}
