namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Booking Suggestion Request DTO for AI Service
/// SECURITY: Only BranchId, Date, CourtType from Frontend
/// Backend fetches BookingHistory and CurrentAvailability server-side using JWT claims
/// </summary>
public class BookingSuggestionRequestDto
{
    public Guid? BranchId { get; set; }
    public DateOnly? Date { get; set; }
    public string? CourtType { get; set; }
    
    // NOTE: UserId, BookingHistory, and CurrentAvailability are NOT sent from Frontend
    // Backend fetches these from database using authenticated user's JWT claims
    // This prevents data tampering and ensures data integrity
}
