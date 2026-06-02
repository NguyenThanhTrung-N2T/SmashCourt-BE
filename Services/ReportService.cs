using Microsoft.Extensions.Caching.Memory;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.Helpers;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class ReportService : IReportService
{
    private readonly IReportRepository _reportRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IBranchScopeResolver _branchScopeResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IReportRepository reportRepo,
        IUserBranchRepository userBranchRepo,
        IBranchScopeResolver branchScopeResolver,
        IMemoryCache cache,
        ILogger<ReportService> logger)
    {
        _reportRepo = reportRepo;
        _userBranchRepo = userBranchRepo;
        _branchScopeResolver = branchScopeResolver;
        _cache = cache;
        _logger = logger;
    }

    #region Helper Methods

    /// <summary>
    /// Validate và normalize date range.
    /// Dùng cho report thông thường và dashboard khi có ít nhất 1 ngày được cung cấp.
    /// KHÔNG được gọi khi isAllTime = true (cả 2 ngày đều null).
    /// - ToDate null  → mặc định hôm nay
    /// - FromDate null → mặc định ToDate - 30 ngày
    /// </summary>
    private (DateOnly fromDate, DateOnly toDate) ValidateDateRange(ReportFilterDto filter)
    {
        var toDate = filter.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = filter.FromDate ?? toDate.AddDays(-30);

        // Validate FromDate <= ToDate
        if (fromDate > toDate)
            throw new AppException(400, "FromDate phải nhỏ hơn hoặc bằng ToDate", ErrorCodes.BadRequest);

        // Validate max 365 days
        var daysDiff = (toDate.ToDateTime(TimeOnly.MinValue) - fromDate.ToDateTime(TimeOnly.MinValue)).Days;
        if (daysDiff > 365)
            throw new AppException(400, "Khoảng thời gian không được vượt quá 365 ngày", ErrorCodes.BadRequest);

        return (fromDate, toDate);
    }

    /// <summary>
    /// Tính khoảng thời gian hiện tại và khoảng tương đương trước đó
    /// </summary>
    private static (
        DateTime currentStart,
        DateTime currentEnd,
        DateTime previousStart,
        DateTime previousEnd
    ) GetComparisonPeriods(ReportFilterDto filter)
    {
        var currentStart = filter.FromDate!.Value.ToDateTime(TimeOnly.MinValue);
        var currentEnd = filter.ToDate!.Value.ToDateTime(TimeOnly.MaxValue);

        var durationDays = (currentEnd - currentStart).TotalDays + 1;
        var previousStart = currentStart.AddDays(-durationDays);
        var previousEnd = currentStart.AddSeconds(-1);

        return (currentStart, currentEnd, previousStart, previousEnd);
    }

    /// <summary>
    /// Tính phần trăm thay đổi, xử lý chia cho 0 an toàn
    /// </summary>
    private static decimal? CalculatePercentageChange(decimal currentValue, decimal previousValue)
    {
        if (previousValue == 0)
            return currentValue > 0 ? 100 : 0;

        return Math.Round(
            (currentValue - previousValue) / previousValue * 100,
            2
        );
    }

    private static decimal? CalculatePercentageChange(int currentValue, int previousValue)
        => CalculatePercentageChange((decimal)currentValue, (decimal)previousValue);

    /// <summary>
    /// Lấy BranchId của BRANCH_MANAGER (null nếu OWNER)
    /// </summary>
    private async Task<Guid?> GetManagerBranchIdAsync(Guid currentUserId, string currentUserRole)
    {
        if (currentUserRole == UserRole.OWNER.ToString())
            return null;

        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (managerBranch == null)
                throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

            return managerBranch.BranchId;
        }

        throw new AppException(403, "Bạn không có quyền truy cập báo cáo", ErrorCodes.Forbidden);
    }

    /// <summary>
    /// Tạo cache key cho dashboard
    /// </summary>
    private static string GetDashboardCacheKey(
        string role, Guid? branchId, DateOnly? fromDate, DateOnly? toDate, string? groupBy)
    {
        return $"dashboard_{role}_{branchId}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}_{groupBy ?? "day"}";
    }

    #endregion

    #region Dashboard

    /// <summary>
    /// Lấy dashboard cho OWNER (toàn hệ thống)
    /// </summary>
    public async Task<OwnerDashboardDto> GetOwnerDashboardAsync(
        ReportFilterDto filter, Guid currentUserId)
    {
        // ALL TIME mode: no date filter → use null dates for repo, skip comparison
        var isAllTime = filter.FromDate == null && filter.ToDate == null;

        DateOnly fromDate, toDate;
        if (isAllTime)
        {
            fromDate = DateOnly.MinValue;
            toDate = DateOnly.MaxValue;
        }
        else
        {
            (fromDate, toDate) = ValidateDateRange(filter);
        }

        // Cache key
        var cacheKey = GetDashboardCacheKey("OWNER", filter.BranchId, filter.FromDate, filter.ToDate, filter.GroupBy);

        // Try get from cache
        if (_cache.TryGetValue(cacheKey, out OwnerDashboardDto? cachedData) && cachedData != null)
        {
            _logger.LogInformation("Dashboard cache HIT for {CacheKey}", cacheKey);
            return cachedData;
        }

        _logger.LogInformation("Dashboard cache MISS for {CacheKey}", cacheKey);

        // Query current period
        var summary = await _reportRepo.GetDashboardSummaryAsync(fromDate, toDate, filter.BranchId, isAllTime);

        // Percentage changes — only when a date range is provided
        if (!isAllTime)
        {
            var (_, _, prevStart, prevEnd) = GetComparisonPeriods(filter);
            var prevFromDate = DateOnly.FromDateTime(prevStart);
            var prevToDate = DateOnly.FromDateTime(prevEnd.Date);

            var prev = await _reportRepo.GetDashboardSummaryAsync(
                prevFromDate, prevToDate, filter.BranchId, isAllTime: false);

            summary.RevenueChangePercent = CalculatePercentageChange(summary.TotalRevenue, prev.TotalRevenue);
            summary.BookingChangePercent = CalculatePercentageChange(summary.TotalBookings, prev.TotalBookings);
            summary.OccupancyChangePercent = CalculatePercentageChange(summary.OccupancyRate, prev.OccupancyRate);
            summary.NewCustomerChangePercent = CalculatePercentageChange(summary.NewCustomers, prev.NewCustomers);
        }

        // TopBranches: Chỉ hiển thị khi KHÔNG filter theo branch cụ thể
        var topBranches = filter.BranchId.HasValue
            ? new List<TopBranchDto>()
            : await _reportRepo.GetTopBranchesAsync(fromDate, toDate, 5);

        var topCustomers = await _reportRepo.GetTopCustomersAsync(fromDate, toDate, filter.BranchId, 5);
        var revenueTrend = await _reportRepo.GetRevenueTrendAsync(fromDate, toDate, filter.BranchId, filter.GroupBy, isAllTime);
        var bookingTrend = await _reportRepo.GetBookingTrendAsync(fromDate, toDate, filter.BranchId, isAllTime);

        var dashboard = new OwnerDashboardDto
        {
            Summary = summary,
            TopBranches = topBranches,
            TopCustomers = topCustomers,
            RevenueTrend = revenueTrend,
            BookingTrend = bookingTrend
        };

        // Cache for 5 minutes
        _cache.Set(cacheKey, dashboard, TimeSpan.FromMinutes(5));

        return dashboard;
    }

    /// <summary>
    /// Lấy dashboard cho BRANCH_MANAGER hoac STAFF (chỉ chi nhánh mình)
    /// </summary>
    public async Task<OperationalManagerDashboardDto> GetOperationalManagerDashboardAsync(
        Guid currentUserId, string currentUserRole)
    {
        if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
        {
            throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);
        }
        var resolvedBranchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(null,
            currentUserId,
            roleEnum);

        var now = DateTimeHelper.GetVietnamNow();
        var today = DateTimeHelper.GetTodayInVietnam();

        var branchInfo = await _reportRepo.GetManagerDashboardBranchInfoAsync(resolvedBranchId);
        var kpis = await _reportRepo.GetManagerDashboardKpisAsync(resolvedBranchId, today, now);
        var liveCourts = await _reportRepo.GetManagerDashboardLiveCourtsAsync(resolvedBranchId, today, now);
        var upcomingBookings = await _reportRepo.GetManagerDashboardUpcomingBookingsAsync(resolvedBranchId, today, now);
        var actionQueue = await _reportRepo.GetManagerDashboardActionQueueAsync(resolvedBranchId, today);
        var occupancyForecast = await _reportRepo.GetManagerDashboardOccupancyForecastAsync(resolvedBranchId, today, now);

        return new OperationalManagerDashboardDto
        {
            BranchId = branchInfo.BranchId,
            BranchName = branchInfo.BranchName,
            GeneratedAt = now,
            Kpis = kpis,
            LiveCourts = liveCourts,
            TotalCourts = branchInfo.TotalCourts,
            UpcomingBookings = upcomingBookings,
            ActionQueue = actionQueue,
            OccupancyForecast = occupancyForecast
        };
    }
    public async Task<ManagerDashboardDto> GetManagerDashboardAsync(
    ReportFilterDto filter, Guid currentUserId)
    {
        // Lấy branchId của manager (bắt buộc)
        var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
        if (managerBranch == null)
            throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

        var branchId = managerBranch.BranchId;

        // ALL TIME mode
        var isAllTime = filter.FromDate == null && filter.ToDate == null;

        DateOnly fromDate, toDate;
        if (isAllTime)
        {
            fromDate = DateOnly.MinValue;
            toDate = DateOnly.MaxValue;
        }
        else
        {
            (fromDate, toDate) = ValidateDateRange(filter);
        }

        // Cache key
        var cacheKey = GetDashboardCacheKey("MANAGER", branchId, filter.FromDate, filter.ToDate, filter.GroupBy);

        // Try get from cache
        if (_cache.TryGetValue(cacheKey, out ManagerDashboardDto? cachedData) && cachedData != null)
        {
            _logger.LogInformation("Dashboard cache HIT for {CacheKey}", cacheKey);
            return cachedData;
        }

        _logger.LogInformation("Dashboard cache MISS for {CacheKey}", cacheKey);

        // Query current period (chỉ chi nhánh mình)
        var summary = await _reportRepo.GetDashboardSummaryAsync(fromDate, toDate, branchId, isAllTime);

        // Percentage changes — only when a date range is provided
        if (!isAllTime)
        {
            var (_, _, prevStart, prevEnd) = GetComparisonPeriods(filter);
            var prevFromDate = DateOnly.FromDateTime(prevStart);
            var prevToDate = DateOnly.FromDateTime(prevEnd.Date);

            var prev = await _reportRepo.GetDashboardSummaryAsync(
                prevFromDate, prevToDate, branchId, isAllTime: false);

            summary.RevenueChangePercent = CalculatePercentageChange(summary.TotalRevenue, prev.TotalRevenue);
            summary.BookingChangePercent = CalculatePercentageChange(summary.TotalBookings, prev.TotalBookings);
            summary.OccupancyChangePercent = CalculatePercentageChange(summary.OccupancyRate, prev.OccupancyRate);
            summary.NewCustomerChangePercent = CalculatePercentageChange(summary.NewCustomers, prev.NewCustomers);
        }

        var topCustomers = await _reportRepo.GetTopCustomersAsync(fromDate, toDate, branchId, 5);
        var revenueTrend = await _reportRepo.GetRevenueTrendAsync(fromDate, toDate, branchId, filter.GroupBy, isAllTime);
        var bookingTrend = await _reportRepo.GetBookingTrendAsync(fromDate, toDate, branchId, isAllTime);

        var dashboard = new ManagerDashboardDto
        {
            Summary = summary,
            TopCustomers = topCustomers,
            RevenueTrend = revenueTrend,
            BookingTrend = bookingTrend
        };

        // Cache for 5 minutes
        _cache.Set(cacheKey, dashboard, TimeSpan.FromMinutes(5));

        return dashboard;
    }

    #endregion

    #region Revenue Report

    /// <summary>
    /// Lấy báo cáo doanh thu
    /// </summary>
    public async Task<RevenueReportDto> GetRevenueReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);

        // BRANCH_MANAGER: Bắt buộc filter theo chi nhánh mình, ignore filter.BranchId
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetRevenueReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion

    #region Booking Report

    /// <summary>
    /// Lấy báo cáo booking
    /// </summary>
    public async Task<BookingReportDto> GetBookingReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);

        // BRANCH_MANAGER: Bắt buộc filter theo chi nhánh mình, ignore filter.BranchId
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetBookingReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion

    #region Court Utilization Report

    /// <summary>
    /// Lấy báo cáo sử dụng sân
    /// </summary>
    public async Task<CourtUtilizationReportDto> GetCourtUtilizationReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetCourtUtilizationReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion

    #region Customer Statistics Report

    /// <summary>
    /// Lấy báo cáo thống kê khách hàng
    /// </summary>
    public async Task<CustomerStatisticsReportDto> GetCustomerStatisticsReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetCustomerStatisticsReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion

    #region Top Spenders Report

    /// <summary>
    /// Lấy báo cáo top khách hàng chi tiêu
    /// </summary>
    public async Task<TopSpendersReportDto> GetTopSpendersReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole, int page, int pageSize)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var branchId = managerBranchId ?? filter.BranchId;

        // Validate pagination
        if (page < 1)
            throw new AppException(400, "Page phải lớn hơn hoặc bằng 1", ErrorCodes.BadRequest);

        if (pageSize < 1 || pageSize > 100)
            throw new AppException(400, "PageSize phải từ 1 đến 100", ErrorCodes.BadRequest);

        return await _reportRepo.GetTopSpendersReportAsync(fromDate, toDate, branchId, page, pageSize);
    }

    #endregion

    #region Service Performance Report

    /// <summary>
    /// Lấy báo cáo hiệu suất dịch vụ
    /// </summary>
    public async Task<ServicePerformanceReportDto> GetServicePerformanceReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetServicePerformanceReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion

    #region Promotion Effectiveness Report

    /// <summary>
    /// Lấy báo cáo hiệu quả khuyến mãi
    /// </summary>
    public async Task<PromotionEffectivenessReportDto> GetPromotionEffectivenessReportAsync(
        ReportFilterDto filter, Guid currentUserId, string currentUserRole)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);
        var managerBranchId = await GetManagerBranchIdAsync(currentUserId, currentUserRole);
        var branchId = managerBranchId ?? filter.BranchId;

        return await _reportRepo.GetPromotionEffectivenessReportAsync(fromDate, toDate, branchId, filter.GroupBy);
    }

    #endregion
}
