namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo top khách hàng chi tiêu nhiều nhất (có pagination)
/// </summary>
public class TopSpendersReportDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<TopSpenderDto> Items { get; set; } = [];
}

/// <summary>
/// Chi tiết khách hàng chi tiêu cao
/// </summary>
public class TopSpenderDto
{
    public Guid CustomerId { get; set; }
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public decimal TotalRevenue { get; set; }
    public int BookingCount { get; set; }
    public string LoyaltyTier { get; set; } = null!;
}
