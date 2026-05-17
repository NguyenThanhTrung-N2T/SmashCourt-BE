namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo hiệu suất dịch vụ
/// </summary>
public class ServicePerformanceReportDto
{
    public decimal TotalServiceRevenue { get; set; }
    public int TotalBookingsWithServices { get; set; }
    public decimal ServiceAttachmentRate { get; set; }
    public decimal AverageServiceRevenuePerBooking { get; set; }
    public List<ServiceItemDto> TopServices { get; set; } = [];
    public List<ServiceTrendDto> ServiceTrend { get; set; } = [];
}

/// <summary>
/// Chi tiết dịch vụ
/// </summary>
public class ServiceItemDto
{
    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
    public decimal AverageRevenue { get; set; }
}

/// <summary>
/// Xu hướng doanh thu dịch vụ
/// </summary>
public class ServiceTrendDto
{
    public string Period { get; set; } = null!;
    public decimal ServiceRevenue { get; set; }
    public int BookingCount { get; set; }
}
