using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// AI Data Preparation Service - Aggregates and sanitizes data for AI Service
/// Fetches data from ReportService, removes PII, applies role-based filtering
/// </summary>
public class AIDataPreparationService
{
    private readonly ILogger<AIDataPreparationService> _logger;
    private readonly IReportService _reportService;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserBranchRepository _userBranchRepo;

    public AIDataPreparationService(
        ILogger<AIDataPreparationService> logger,
        IReportService reportService,
        IBookingRepository bookingRepository,
        IUserBranchRepository userBranchRepo)
    {
        _logger = logger;
        _reportService = reportService;
        _bookingRepository = bookingRepository;
        _userBranchRepo = userBranchRepo;
    }

    /// <summary>
    /// Prepare booking history for AI analysis
    /// Fetches user booking history, transforms to patterns (dayOfWeek, timeSlot, courtType, price)
    /// Removes internal IDs (BookingId, CourtId) for security
    /// </summary>
    /// <param name="userId">User ID to fetch booking history for</param>
    /// <param name="maxRecords">Maximum number of records to return (default: 50)</param>
    /// <returns>Sanitized booking history data</returns>
    public async Task<BookingHistoryData> PrepareBookingHistoryAsync(Guid userId, int maxRecords = 50)
    {
        _logger.LogInformation("Preparing booking history for user {UserId}, maxRecords: {MaxRecords}", userId, maxRecords);

        // Fetch user's booking history
        var query = new DTOs.Booking.BookingListQuery
        {
            Page = 1,
            PageSize = maxRecords,
            Status = null, // Get all statuses
            SortBy = "BookingDate",
            SortOrder = "desc"
        };

        var bookingsPage = await _bookingRepository.GetByCustomerIdAsync(userId, query);
        var bookings = bookingsPage.Items;

        // Transform to patterns (remove internal IDs)
        var patterns = bookings
            .Where(b => b.Status == BookingStatus.COMPLETED) // Only completed bookings for pattern analysis
            .SelectMany(b => b.BookingCourts.Select(bc => new BookingPattern
            {
                BranchName = b.Branch.Name,
                CourtType = bc.Court.CourtType.Name,
                DayOfWeek = b.BookingDate.DayOfWeek.ToString(),
                TimeSlot = $"{bc.StartTime:HH:mm}-{bc.EndTime:HH:mm}",
                Price = bc.BookingPriceItems.Sum(pi => pi.UnitPrice), // Fixed: Use UnitPrice instead of Price
                BookingDate = b.BookingDate.ToString("yyyy-MM-dd")
            }))
            .ToList();

        _logger.LogInformation("Prepared {Count} booking patterns for user {UserId}", patterns.Count, userId);

        return new BookingHistoryData
        {
            TotalBookings = bookings.Count(),
            CompletedBookings = bookings.Count(b => b.Status == BookingStatus.COMPLETED),
            Patterns = patterns
        };
    }

    /// <summary>
    /// Prepare occupancy data for AI analysis
    /// Aggregates occupancy rates by time slot
    /// Applies role-based filtering (BRANCH_MANAGER sees only their branch)
    /// </summary>
    /// <param name="branchId">Branch ID to filter by (null for all branches)</param>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <returns>Aggregated occupancy data</returns>
    public async Task<OccupancyData> PrepareOccupancyDataAsync(Guid? branchId, DateOnly fromDate, DateOnly toDate)
    {
        _logger.LogInformation("Preparing occupancy data for branchId: {BranchId}, from: {FromDate}, to: {ToDate}", 
            branchId, fromDate, toDate);

        var filter = new ReportFilterDto
        {
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            GroupBy = "day"
        };

        // Fetch court utilization report (contains occupancy data)
        var utilizationReport = await _reportService.GetCourtUtilizationReportAsync(
            filter, 
            Guid.Empty, // Not used in this context
            UserRole.OWNER.ToString()); // Use OWNER role to bypass branch filtering

        // Transform to occupancy patterns from Items
        var occupancyPatterns = utilizationReport.Items
            .Select(d => new OccupancyPattern
            {
                Period = d.Period ?? "Unknown",
                OccupancyRate = d.OccupancyRate,
                TotalSlots = (int)d.AvailableHours, // Convert hours to slots approximation
                BookedSlots = (int)d.BookedHours
            })
            .ToList();

        return new OccupancyData
        {
            FromDate = fromDate.ToString("yyyy-MM-dd"),
            ToDate = toDate.ToString("yyyy-MM-dd"),
            AverageOccupancyRate = utilizationReport.OverallOccupancyRate,
            Patterns = occupancyPatterns
        };
    }

    /// <summary>
    /// Prepare revenue data for AI analysis
    /// Aggregates revenue trends by time period
    /// Applies role-based filtering (BRANCH_MANAGER sees only their branch)
    /// </summary>
    /// <param name="branchId">Branch ID to filter by (null for all branches)</param>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <returns>Aggregated revenue data</returns>
    public async Task<RevenueData> PrepareRevenueDataAsync(Guid? branchId, DateOnly fromDate, DateOnly toDate)
    {
        _logger.LogInformation("Preparing revenue data for branchId: {BranchId}, from: {FromDate}, to: {ToDate}", 
            branchId, fromDate, toDate);

        var filter = new ReportFilterDto
        {
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            GroupBy = "day"
        };

        // Fetch revenue report
        var revenueReport = await _reportService.GetRevenueReportAsync(
            filter,
            Guid.Empty, // Not used in this context
            UserRole.OWNER.ToString()); // Use OWNER role to bypass branch filtering

        // Transform to revenue patterns from Items
        var revenuePatterns = revenueReport.Items
            .Select(d => new RevenuePattern
            {
                Period = d.Period,
                Revenue = d.Revenue,
                BookingCount = d.BookingCount,
                AverageRevenuePerBooking = d.BookingCount > 0 ? d.Revenue / d.BookingCount : 0
            })
            .ToList();

        return new RevenueData
        {
            FromDate = fromDate.ToString("yyyy-MM-dd"),
            ToDate = toDate.ToString("yyyy-MM-dd"),
            TotalRevenue = revenueReport.TotalRevenue,
            AverageRevenuePerBooking = revenueReport.AverageBookingValue,
            Patterns = revenuePatterns
        };
    }

    /// <summary>
    /// Prepare dashboard data for AI analysis
    /// Fetches dashboard metrics and trends
    /// Applies role-based filtering (BRANCH_MANAGER sees only their branch)
    /// </summary>
    /// <param name="branchId">Branch ID to filter by (null for all branches)</param>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <returns>Dashboard data</returns>
    public async Task<DashboardData> PrepareDashboardDataAsync(Guid? branchId, DateOnly fromDate, DateOnly toDate)
    {
        _logger.LogInformation("Preparing dashboard data for branchId: {BranchId}, from: {FromDate}, to: {ToDate}", 
            branchId, fromDate, toDate);

        var filter = new ReportFilterDto
        {
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate
        };

        // Fetch dashboard data based on branch filter
        if (branchId.HasValue)
        {
            // Manager dashboard (single branch)
            var managerDashboard = await _reportService.GetManagerDashboardAsync(filter, Guid.Empty);
            
            return new DashboardData
            {
                FromDate = fromDate.ToString("yyyy-MM-dd"),
                ToDate = toDate.ToString("yyyy-MM-dd"),
                Summary = new DashboardSummary
                {
                    TotalRevenue = managerDashboard.Summary.TotalRevenue,
                    TotalBookings = managerDashboard.Summary.TotalBookings,
                    CompletedBookings = managerDashboard.Summary.CompletedBookings,
                    CancelledBookings = managerDashboard.Summary.CancelledBookings,
                    OccupancyRate = managerDashboard.Summary.OccupancyRate,
                    NewCustomers = managerDashboard.Summary.NewCustomers
                },
                RevenueTrend = managerDashboard.RevenueTrend.Select(rt => new RevenueTrendPattern
                {
                    Period = rt.Period,
                    Revenue = rt.Revenue,
                    BookingCount = rt.BookingCount
                }).ToList(),
                BookingTrend = managerDashboard.BookingTrend.Select(bt => new BookingTrendPattern
                {
                    Period = bt.Period,
                    TotalCount = bt.TotalCount,
                    CompletedCount = bt.CompletedCount
                }).ToList()
            };
        }
        else
        {
            // Owner dashboard (all branches)
            var ownerDashboard = await _reportService.GetOwnerDashboardAsync(filter, Guid.Empty);
            
            return new DashboardData
            {
                FromDate = fromDate.ToString("yyyy-MM-dd"),
                ToDate = toDate.ToString("yyyy-MM-dd"),
                Summary = new DashboardSummary
                {
                    TotalRevenue = ownerDashboard.Summary.TotalRevenue,
                    TotalBookings = ownerDashboard.Summary.TotalBookings,
                    CompletedBookings = ownerDashboard.Summary.CompletedBookings,
                    CancelledBookings = ownerDashboard.Summary.CancelledBookings,
                    OccupancyRate = ownerDashboard.Summary.OccupancyRate,
                    NewCustomers = ownerDashboard.Summary.NewCustomers
                },
                RevenueTrend = ownerDashboard.RevenueTrend.Select(rt => new RevenueTrendPattern
                {
                    Period = rt.Period,
                    Revenue = rt.Revenue,
                    BookingCount = rt.BookingCount
                }).ToList(),
                BookingTrend = ownerDashboard.BookingTrend.Select(bt => new BookingTrendPattern
                {
                    Period = bt.Period,
                    TotalCount = bt.TotalCount,
                    CompletedCount = bt.CompletedCount
                }).ToList(),
                TopBranches = ownerDashboard.TopBranches.Select(tb => new TopBranchPattern
                {
                    BranchName = tb.BranchName,
                    Revenue = tb.Revenue,
                    BookingCount = tb.BookingCount
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Prepare cross-branch performance data for AI analysis
    /// Aggregates performance metrics across all branches
    /// Only accessible by OWNER role
    /// </summary>
    /// <param name="fromDate">Start date</param>
    /// <param name="toDate">End date</param>
    /// <returns>Cross-branch performance data</returns>
    public async Task<CrossBranchData> PrepareCrossBranchDataAsync(DateOnly fromDate, DateOnly toDate)
    {
        _logger.LogInformation("Preparing cross-branch data from: {FromDate}, to: {ToDate}", fromDate, toDate);

        var filter = new ReportFilterDto
        {
            BranchId = null, // All branches
            FromDate = fromDate,
            ToDate = toDate
        };

        // Fetch owner dashboard (contains top branches)
        var ownerDashboard = await _reportService.GetOwnerDashboardAsync(filter, Guid.Empty);

        // Transform to cross-branch patterns
        var branchPerformance = ownerDashboard.TopBranches
            .Select(tb => new BranchPerformancePattern
            {
                BranchName = tb.BranchName,
                Revenue = tb.Revenue,
                BookingCount = tb.BookingCount,
                AverageRevenuePerBooking = tb.BookingCount > 0 ? tb.Revenue / tb.BookingCount : 0
            })
            .ToList();

        return new CrossBranchData
        {
            FromDate = fromDate.ToString("yyyy-MM-dd"),
            ToDate = toDate.ToString("yyyy-MM-dd"),
            TotalBranches = branchPerformance.Count,
            TotalRevenue = ownerDashboard.Summary.TotalRevenue,
            TotalBookings = ownerDashboard.Summary.TotalBookings,
            BranchPerformance = branchPerformance
        };
    }

    /// <summary>
    /// Build chat context for AI chatbot
    /// Includes user role, assigned branch (for managers), recent booking count
    /// Does NOT include sensitive data or other users' information
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="userRole">User role</param>
    /// <returns>Chat context string</returns>
    public async Task<string> BuildChatContextAsync(Guid userId, string userRole)
    {
        _logger.LogInformation("Building chat context for user {UserId}, role: {UserRole}", userId, userRole);

        var contextParts = new List<string>
        {
            $"User Role: {userRole}"
        };

        // Add branch context for managers
        if (userRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(userId);
            if (managerBranch != null)
            {
                contextParts.Add($"Assigned Branch: {managerBranch.Branch.Name}");
            }
        }

        // Add recent booking count for customers
        if (userRole == UserRole.CUSTOMER.ToString())
        {
            var completedBookingCount = await _bookingRepository.GetCompletedBookingCountAsync(userId);
            contextParts.Add($"Completed Bookings: {completedBookingCount}");
        }

        var context = string.Join(", ", contextParts);
        _logger.LogInformation("Built chat context: {Context}", context);

        return context;
    }
}

#region Data Transfer Objects

/// <summary>
/// Booking history data for AI analysis
/// </summary>
public class BookingHistoryData
{
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public List<BookingPattern> Patterns { get; set; } = [];
}

/// <summary>
/// Booking pattern (no internal IDs)
/// </summary>
public class BookingPattern
{
    public string BranchName { get; set; } = null!;
    public string CourtType { get; set; } = null!;
    public string DayOfWeek { get; set; } = null!;
    public string TimeSlot { get; set; } = null!;
    public decimal Price { get; set; }
    public string BookingDate { get; set; } = null!;
}

/// <summary>
/// Occupancy data for AI analysis
/// </summary>
public class OccupancyData
{
    public string FromDate { get; set; } = null!;
    public string ToDate { get; set; } = null!;
    public decimal AverageOccupancyRate { get; set; }
    public List<OccupancyPattern> Patterns { get; set; } = [];
}

/// <summary>
/// Occupancy pattern
/// </summary>
public class OccupancyPattern
{
    public string Period { get; set; } = null!;
    public decimal OccupancyRate { get; set; }
    public int TotalSlots { get; set; }
    public int BookedSlots { get; set; }
}

/// <summary>
/// Revenue data for AI analysis
/// </summary>
public class RevenueData
{
    public string FromDate { get; set; } = null!;
    public string ToDate { get; set; } = null!;
    public decimal TotalRevenue { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }
    public List<RevenuePattern> Patterns { get; set; } = [];
}

/// <summary>
/// Revenue pattern
/// </summary>
public class RevenuePattern
{
    public string Period { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }
}

/// <summary>
/// Dashboard data for AI analysis
/// </summary>
public class DashboardData
{
    public string FromDate { get; set; } = null!;
    public string ToDate { get; set; } = null!;
    public DashboardSummary Summary { get; set; } = null!;
    public List<RevenueTrendPattern> RevenueTrend { get; set; } = [];
    public List<BookingTrendPattern> BookingTrend { get; set; } = [];
    public List<TopBranchPattern> TopBranches { get; set; } = [];
}

/// <summary>
/// Dashboard summary
/// </summary>
public class DashboardSummary
{
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public decimal OccupancyRate { get; set; }
    public int NewCustomers { get; set; }
}

/// <summary>
/// Revenue trend pattern
/// </summary>
public class RevenueTrendPattern
{
    public string Period { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Booking trend pattern
/// </summary>
public class BookingTrendPattern
{
    public string Period { get; set; } = null!;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
}

/// <summary>
/// Top branch pattern
/// </summary>
public class TopBranchPattern
{
    public string BranchName { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
}

/// <summary>
/// Cross-branch performance data
/// </summary>
public class CrossBranchData
{
    public string FromDate { get; set; } = null!;
    public string ToDate { get; set; } = null!;
    public int TotalBranches { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalBookings { get; set; }
    public List<BranchPerformancePattern> BranchPerformance { get; set; } = [];
}

/// <summary>
/// Branch performance pattern
/// </summary>
public class BranchPerformancePattern
{
    public string BranchName { get; set; } = null!;
    public decimal Revenue { get; set; }
    public int BookingCount { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }
}

#endregion
