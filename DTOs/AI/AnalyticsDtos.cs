namespace SmashCourt_BE.Services.AI.DTOs;

/// <summary>
/// DTOs for FastAPI Analytics endpoints (summary, strategic)
/// </summary>

#region Analytics Summary DTOs

internal sealed class FastApiAnalyticsRequest
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public FastApiAnalyticsMetrics Metrics { get; set; } = new();
}

internal sealed class FastApiAnalyticsMetrics
{
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public int CancelledBookings { get; set; }
    public decimal CancellationRate { get; set; }
    public decimal AvgOccupancyRate { get; set; }
    public List<string> PeakHours { get; set; } = [];
    public List<string> LowHours { get; set; } = [];
    public List<object> RevenueByCourtType { get; set; } = [];
    public List<object> TopPromotions { get; set; } = [];
}

#endregion

#region Strategic Suggestion DTOs

internal sealed class FastApiStrategicSuggestionRequest
{
    public string Period { get; set; } = string.Empty;
    public int TotalBranches { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public List<FastApiStrategicBranchPerformanceItem> BranchPerformances { get; set; } = [];
}

internal sealed class FastApiStrategicBranchPerformanceItem
{
    public string BranchName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }
}

#endregion
