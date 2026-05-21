namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO for sending booking history patterns to AI Service.
/// Excludes BookingId and internal identifiers for security.
/// Only includes patterns needed for AI analysis.
/// </summary>
internal class BookingHistoryItemForAiDto
{
    public string BranchName { get; set; } = string.Empty;
    public string CourtType { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;  // "Monday", "Tuesday", etc.
    public string TimeSlot { get; set; } = string.Empty;    // "06:00-08:00"
    public decimal Price { get; set; }
    // NOTE: BookingId removed - AI Service doesn't need internal IDs
}
