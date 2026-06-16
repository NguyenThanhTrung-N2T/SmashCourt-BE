namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo sử dụng sân
/// </summary>
public class CourtUtilizationReportDto
{
    public decimal OverallOccupancyRate { get; set; }
    public decimal TotalAvailableHours { get; set; }
    public decimal TotalBookedHours { get; set; }
    public List<PeakHourDto> PeakHours { get; set; } = [];
    public List<PeakHourDto> OffPeakHours { get; set; } = [];
    public List<CourtUtilizationItemDto> TopCourts { get; set; } = [];
    public List<CourtUtilizationItemDto> Items { get; set; } = [];
}

/// <summary>
/// DTO giờ cao điểm / thấp điểm
/// </summary>
public class PeakHourDto
{
    public int Hour { get; set; }
    public int BookingCount { get; set; }
    public decimal OccupancyRate { get; set; }
}

/// <summary>
/// Chi tiết sử dụng sân theo nhóm
/// </summary>
public class CourtUtilizationItemDto
{
    public Guid? CourtId { get; set; }
    public string? CourtName { get; set; }
    public string? Period { get; set; }
    public decimal BookedHours { get; set; }
    public decimal AvailableHours { get; set; }
    public decimal OccupancyRate { get; set; }
}
