using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Strategic Suggestion Request DTO for AI Service (OWNER only)
/// </summary>
public class StrategicSuggestionRequestDto
{
    [Required(ErrorMessage = "FromDate is required")]
    public DateOnly FromDate { get; set; }
    
    [Required(ErrorMessage = "ToDate is required")]
    public DateOnly ToDate { get; set; }
}
