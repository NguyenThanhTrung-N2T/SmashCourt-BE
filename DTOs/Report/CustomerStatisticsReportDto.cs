namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo thống kê khách hàng
/// </summary>
public class CustomerStatisticsReportDto
{
    public int TotalCustomers { get; set; }
    public int NewCustomers { get; set; }
    public int RepeatCustomers { get; set; }
    public decimal RepeatCustomerRate { get; set; }
    public decimal AverageBookingsPerCustomer { get; set; }
    public decimal AverageRevenuePerCustomer { get; set; }
    public List<LoyaltyTierDistributionDto> LoyaltyTierDistribution { get; set; } = [];
    public List<CustomerAcquisitionTrendDto> AcquisitionTrend { get; set; } = [];
}

/// <summary>
/// Phân bố khách hàng theo loyalty tier
/// </summary>
public class LoyaltyTierDistributionDto
{
    public string TierName { get; set; } = null!;
    public int CustomerCount { get; set; }
    public decimal Percentage { get; set; }
}

/// <summary>
/// Xu hướng thu hút khách hàng mới
/// </summary>
public class CustomerAcquisitionTrendDto
{
    public string Period { get; set; } = null!;
    public int NewCustomers { get; set; }
}
