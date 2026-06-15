namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// Dashboard DTO for OWNER across the whole system.
/// </summary>
public class OwnerDashboardDto
{
    /// <summary>
    /// Shared analytics summary.
    /// </summary>
    public DashboardSummaryDto Summary { get; set; } = null!;

    /// <summary>
    /// Top 5 branches by revenue.
    /// </summary>
    public List<TopBranchDto> TopBranches { get; set; } = [];

    /// <summary>
    /// Top 5 customers by revenue.
    /// </summary>
    public List<TopCustomerDto> TopCustomers { get; set; } = [];

    /// <summary>
    /// Revenue trend by period.
    /// </summary>
    public List<RevenueTrendDto> RevenueTrend { get; set; } = [];

    /// <summary>
    /// Booking trend by period.
    /// </summary>
    public List<BookingTrendDto> BookingTrend { get; set; } = [];
}
/// <summary>
/// Operational dashboard DTO for BRANCH_MANAGER.
/// </summary>
public class OperationalManagerDashboardDto
{
    /// <summary>
    /// Branch this dashboard belongs to.
    /// </summary>
    public Guid BranchId { get; set; }

    /// <summary>
    /// Branch display name.
    /// </summary>
    public string BranchName { get; set; } = null!;

    /// <summary>
    /// Time when this dashboard snapshot was generated.
    /// </summary>
    public string GeneratedAt { get; set; } = null!;

    /// <summary>
    /// KPI cards shown at the top of the manager dashboard.
    /// </summary>
    public ManagerDashboardKpiDto Kpis { get; set; } = new();

    /// <summary>
    /// Live court cards ordered by attention priority.
    /// Expected page size: 6-8 cards.
    /// </summary>
    public List<LiveCourtAttentionDto> LiveCourts { get; set; } = [];

    /// <summary>
    /// Total number of courts in the branch, used by the "View all N courts" link.
    /// </summary>
    public int TotalCourts { get; set; }

    /// <summary>
    /// Next 10 bookings from now until the end of today.
    /// </summary>
    public List<UpcomingBookingDashboardItemDto> UpcomingBookings { get; set; } = [];

    /// <summary>
    /// Operational queue for actions the manager must handle.
    /// Only action types: PENDING_PAYMENT and CANCELLED_PENDING_REFUND.
    /// </summary>
    public List<ManagerDashboardActionItemDto> ActionQueue { get; set; } = [];

    /// <summary>
    /// Occupancy forecast for the next 8 hours.
    /// </summary>
    public List<OccupancyForecastPointDto> OccupancyForecast { get; set; } = [];

    /// <summary>
    /// TODO: Broadcast dashboard changes to managers after the query contract is implemented.
    /// </summary>
    // public bool BroadcastEnabled { get; set; }
}

/// <summary>
/// Analytics dashboard DTO for BRANCH_MANAGER.
/// </summary>
public class ManagerDashboardDto
{

    /// <summary>
    /// Legacy analytics summary kept temporarily while the manager dashboard service is migrated.
    /// </summary>
    public DashboardSummaryDto Summary { get; set; } = new();

    /// <summary>
    /// Legacy manager analytics field kept temporarily while the manager dashboard service is migrated.
    /// </summary>
    public List<TopCustomerDto> TopCustomers { get; set; } = [];

    /// <summary>
    /// Legacy manager analytics field kept temporarily while the manager dashboard service is migrated.
    /// </summary>
    public List<RevenueTrendDto> RevenueTrend { get; set; } = [];

    /// <summary>
    /// Legacy manager analytics field kept temporarily while the manager dashboard service is migrated.
    /// </summary>
    public List<BookingTrendDto> BookingTrend { get; set; } = [];
}

/// <summary>
/// KPI cards for the branch manager dashboard.
/// </summary>
public class ManagerDashboardKpiDto
{
    public decimal RevenueToday { get; set; }
    public int CourtsInUse { get; set; }
    public int TodayBookingsCount { get; set; }
    public int UpcomingCheckInsCount { get; set; }
    public int NeedsActionCount { get; set; }
    public int PendingPaymentCount { get; set; }
    public int PendingRefundCount { get; set; }
}

/// <summary>
/// Branch metadata for the manager dashboard.
/// </summary>
public class ManagerDashboardBranchInfoDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public int TotalCourts { get; set; }
}

/// <summary>
/// Live court card shown in the manager dashboard.
/// </summary>
public class LiveCourtAttentionDto
{
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = null!;
    public string CourtStatus { get; set; } = null!;

    /// <summary>
    /// Priority status for display.
    /// Values: PENDING_PAYMENT, UPCOMING_CHECK_IN, NO_SHOW_RISK, PLAYING, AVAILABLE.
    /// </summary>
    public string AttentionStatus { get; set; } = null!;

    public Guid? BookingId { get; set; }
    public string? BookingCode { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public decimal? AmountDue { get; set; }
    public string? PaymentStatus { get; set; }
}

/// <summary>
/// Upcoming booking item shown in the next-bookings panel.
/// </summary>
public class UpcomingBookingDashboardItemDto
{
    public Guid BookingId { get; set; }
    public string BookingCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public List<DashboardCourtSlotDto> Courts { get; set; } = [];
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public string BookingStatus { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public decimal FinalTotal { get; set; }
}

/// <summary>
/// Court slot summary used by dashboard booking cards.
/// </summary>
public class DashboardCourtSlotDto
{
    public Guid CourtId { get; set; }
    public string CourtName { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
}

/// <summary>
/// Action item that requires manager handling.
/// </summary>
public class ManagerDashboardActionItemDto
{
    public Guid BookingId { get; set; }
    public string BookingCode { get; set; } = null!;

    /// <summary>
    /// Values: PENDING_PAYMENT, CANCELLED_PENDING_REFUND.
    /// </summary>
    public string ActionType { get; set; } = null!;

    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public List<DashboardCourtSlotDto> Courts { get; set; } = [];
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public decimal Amount { get; set; }
    public string CreatedAt { get; set; } = null!;
}

/// <summary>
/// Forecast point for branch occupancy in the next 8 hours.
/// </summary>
public class OccupancyForecastPointDto
{
    public string Time { get; set; } = default!;
    public int TotalCourts { get; set; }
    public int OccupiedCourts { get; set; }
    public int AvailableCourts { get; set; }
    public int BookingCount { get; set; }
    public decimal OccupancyRate { get; set; }
    public bool IsPeakRisk { get; set; }
}

/// <summary>
/// Shared analytics summary.
/// </summary>
public class DashboardSummaryDto
{
    public decimal TotalRevenue { get; set; }
    public decimal? RevenueChangePercent { get; set; }

    public int TotalBookings { get; set; }
    public decimal? BookingChangePercent { get; set; }

    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int NoShowBookings { get; set; }

    public int NewCustomers { get; set; }
    public decimal? NewCustomerChangePercent { get; set; }

    public decimal OccupancyRate { get; set; }
    public decimal? OccupancyChangePercent { get; set; }

    public decimal OnlinePaymentRevenue { get; set; }
    public decimal CashPaymentRevenue { get; set; }
}

/// <summary>
/// Top branch.
/// </summary>
public class TopBranchDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Top customer.
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
/// Revenue trend by period.
/// </summary>
public class RevenueTrendDto
{
    public string Period { get; set; } = null!;  // YYYY-MM-DD
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Booking trend by period.
/// </summary>
public class BookingTrendDto
{
    public string Period { get; set; } = null!;  // YYYY-MM-DD
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
}
