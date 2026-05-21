using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Pricing Suggestion Request DTO for AI Service
/// </summary>
public class PricingSuggestionRequestDto
{
    public Guid? BranchId { get; set; }
    
    [Required(ErrorMessage = "FromDate is required")]
    public DateOnly FromDate { get; set; }
    
    [Required(ErrorMessage = "ToDate is required")]
    public DateOnly ToDate { get; set; }
}
