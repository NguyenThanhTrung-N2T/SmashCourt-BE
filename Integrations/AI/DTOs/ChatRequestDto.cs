using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Chat Request DTO for AI Service
/// SECURITY: Context field is NOT included - Backend builds context server-side
/// </summary>
public class ChatRequestDto
{
    [Required(ErrorMessage = "Message is required")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Message must be between 1 and 2000 characters")]
    public string Message { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "SessionId cannot exceed 100 characters")]
    public string? SessionId { get; set; }
    
    // NOTE: Context field is intentionally EXCLUDED for security
    // Backend builds context server-side using authenticated user data
}
