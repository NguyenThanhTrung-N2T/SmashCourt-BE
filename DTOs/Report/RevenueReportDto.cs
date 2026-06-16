namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo doanh thu
/// </summary>
public class RevenueReportDto
{
    public decimal TotalRevenue { get; set; }
    public decimal CourtRevenue { get; set; }
    public decimal ServiceRevenue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal AverageBookingValue { get; set; }
    public List<RevenueItemDto> Items { get; set; } = [];
}

/// <summary>
/// Chi tiết doanh thu theo nhóm
/// </summary>
public class RevenueItemDto
{
    public string Period { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}
