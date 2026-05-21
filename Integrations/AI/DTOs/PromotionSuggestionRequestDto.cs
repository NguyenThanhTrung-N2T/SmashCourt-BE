using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Promotion Suggestion Request DTO for AI Service
/// SECURITY: BranchId is nullable - null means all branches (OWNER only)
/// </summary>
public class PromotionSuggestionRequestDto
{
    public Guid? BranchId { get; set; }  // Optional - null means all branches (OWNER only)
    
    [Required(ErrorMessage = "FromDate is required")]
    public DateOnly FromDate { get; set; }
    
    [Required(ErrorMessage = "ToDate is required")]
    public DateOnly ToDate { get; set; }
    
    // Implementation Notes:
    // - If BRANCH_MANAGER: Backend enforces their assigned branch (ignores request BranchId)
    // - If OWNER with BranchId: Suggestions for specific branch
    // - If OWNER with null BranchId: System-wide promotion suggestions across all branches
}
