namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO for sending availability slot patterns to AI Service.
/// Excludes CourtId and internal identifiers for security.
/// Only includes patterns needed for AI analysis.
/// </summary>
internal class AvailabilitySlotForAiDto
{
    public string BranchName { get; set; } = string.Empty;
    public string CourtName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public decimal Price { get; set; }
    // NOTE: CourtId removed - AI Service doesn't need internal IDs
}
