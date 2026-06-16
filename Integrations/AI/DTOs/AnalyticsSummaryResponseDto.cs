namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Analytics Summary Response DTO from AI Service
/// </summary>
public class AnalyticsSummaryResponseDto
{
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
    public List<string> Concerns { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}
