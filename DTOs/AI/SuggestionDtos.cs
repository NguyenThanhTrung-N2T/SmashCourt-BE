namespace SmashCourt_BE.Services.AI.DTOs;

/// <summary>
/// DTOs for FastAPI Suggestion endpoints (booking, pricing, promotion)
/// </summary>

#region Booking Suggestion DTOs

internal sealed class FastApiBookingSuggestionRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public List<FastApiBookingHistoryItem> BookingHistory { get; set; } = [];
    public string? CurrentBranchId { get; set; }
    public string? CurrentBranchName { get; set; }
}

internal sealed class FastApiBookingHistoryItem
{
    public string BookingDate { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string CourtType { get; set; } = string.Empty;
    public List<string> TimeSlots { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

#endregion

#region Pricing Suggestion DTOs

internal sealed class FastApiPricingSuggestionRequest
{
    public string? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Period { get; set; } = string.Empty;
    public decimal AverageOccupancyRate { get; set; }
    public List<FastApiOccupancyPatternItem> OccupancyPatterns { get; set; } = [];
    public List<FastApiRevenuePatternItem> RevenuePatterns { get; set; } = [];
}

#endregion

#region Promotion Suggestion DTOs

internal sealed class FastApiPromotionSuggestionRequest
{
    public string? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Period { get; set; } = string.Empty;
    public List<FastApiOccupancyPatternItem> OccupancyPatterns { get; set; } = [];
    public List<FastApiRevenuePatternItem> RevenuePatterns { get; set; } = [];
}

#endregion

#region Shared Pattern DTOs

internal sealed class FastApiOccupancyPatternItem
{
    public string Period { get; set; } = string.Empty;
    public decimal OccupancyRate { get; set; }
    public int TotalSlots { get; set; }
    public int BookedSlots { get; set; }
}

internal sealed class FastApiRevenuePatternItem
{
    public string Period { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }
}

#endregion
