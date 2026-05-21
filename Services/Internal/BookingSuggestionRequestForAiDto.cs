namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO for sending booking suggestion requests to AI Service.
/// Contains sanitized booking history and availability patterns.
/// Excludes UserId - AI Service doesn't need to know user identity.
/// </summary>
internal class BookingSuggestionRequestForAiDto
{
    public List<BookingHistoryItemForAiDto> BookingHistory { get; set; } = new();
    public List<AvailabilitySlotForAiDto> CurrentAvailability { get; set; } = new();
    public string? PreferredBranch { get; set; }
    public string? PreferredDate { get; set; }
    public string? PreferredCourtType { get; set; }
    // NOTE: UserId removed - AI Service doesn't need to know user identity
}
