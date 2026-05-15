namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO dashboard cho OWNER (toàn hệ thống)
/// </summary>
public class OwnerDashboardDto
{
    /// <summary>
    /// Tổng quan metrics
    /// </summary>
    public DashboardSummaryDto Summary { get; set; } = null!;
    
    /// <summary>
    /// Top 5 chi nhánh theo doanh thu
    /// </summary>
    public List<TopBranchDto> TopBranches { get; set; } = [];
    
    /// <summary>
    /// Top 5 khách hàng theo doanh thu
    /// </summary>
    public List<TopCustomerDto> TopCustomers { get; set; } = [];
    
    /// <summary>
    /// Xu hướng doanh thu theo ngày
    /// </summary>
    public List<RevenueTrendDto> RevenueTrend { get; set; } = [];
    
    /// <summary>
    /// Xu hướng booking theo ngày
    /// </summary>
    public List<BookingTrendDto> BookingTrend { get; set; } = [];
}

/// <summary>
/// DTO dashboard cho BRANCH_MANAGER (chỉ chi nhánh mình)
/// </summary>
public class ManagerDashboardDto
{
    /// <summary>
    /// Tổng quan metrics
    /// </summary>
    public DashboardSummaryDto Summary { get; set; } = null!;
    
    /// <summary>
    /// Top 5 khách hàng theo doanh thu
    /// </summary>
    public List<TopCustomerDto> TopCustomers { get; set; } = [];
    
    /// <summary>
    /// Xu hướng doanh thu theo ngày
    /// </summary>
    public List<RevenueTrendDto> RevenueTrend { get; set; } = [];
    
    /// <summary>
    /// Xu hướng booking theo ngày
    /// </summary>
    public List<BookingTrendDto> BookingTrend { get; set; } = [];
}

/// <summary>
/// Tổng quan metrics chung
/// </summary>
public class DashboardSummaryDto
{
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int NoShowBookings { get; set; }
    public int NewCustomers { get; set; }
    public decimal OccupancyRate { get; set; }
    public decimal OnlinePaymentRevenue { get; set; }
    public decimal CashPaymentRevenue { get; set; }
}

/// <summary>
/// Top chi nhánh
/// </summary>
public class TopBranchDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Top khách hàng
/// </summary>
public class TopCustomerDto
{
    public Guid CustomerId { get; set; }
    public string FullName { get; set; } = null!;
    public decimal TotalRevenue { get; set; }
    public int BookingCount { get; set; }
    public string LoyaltyTier { get; set; } = null!;
}

/// <summary>
/// Xu hướng doanh thu theo thời gian
/// </summary>
public class RevenueTrendDto
{
    public string Period { get; set; } = null!;  // YYYY-MM-DD
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Xu hướng booking theo thời gian
/// </summary>
public class BookingTrendDto
{
    public string Period { get; set; } = null!;  // YYYY-MM-DD
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
}
