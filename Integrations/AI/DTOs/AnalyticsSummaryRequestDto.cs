using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Analytics Summary Request DTO for AI Service
/// </summary>
public class AnalyticsSummaryRequestDto
{
    public Guid? BranchId { get; set; }
    
    [Required(ErrorMessage = "FromDate is required")]
    public DateOnly FromDate { get; set; }
    
    [Required(ErrorMessage = "ToDate is required")]
    public DateOnly ToDate { get; set; }
}
