using Microsoft.Extensions.Caching.Memory;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class ReportService : IReportService
{
    private readonly IReportRepository _reportRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IReportRepository reportRepo,
        IUserBranchRepository userBranchRepo,
        IMemoryCache cache,
        ILogger<ReportService> logger)
    {
        _reportRepo = reportRepo;
        _userBranchRepo = userBranchRepo;
        _cache = cache;
        _logger = logger;
    }

    #region Helper Methods

    /// <summary>
    /// Validate và normalize date range
    /// Default: 30 ngày gần nhất
    /// </summary>
    private (DateOnly fromDate, DateOnly toDate) ValidateDateRange(ReportFilterDto filter)
    {
        var toDate = filter.ToDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = filter.FromDate ?? toDate.AddDays(-30);

        // Validate FromDate <= ToDate
        if (fromDate > toDate)
        {
            throw new AppException(400, "FromDate phải nhỏ hơn hoặc bằng ToDate", ErrorCodes.BadRequest);
        }

        // Validate max 365 days
        var daysDiff = (toDate.ToDateTime(TimeOnly.MinValue) - fromDate.ToDateTime(TimeOnly.MinValue)).Days;
        if (daysDiff > 365)
        {
            throw new AppException(400, "Khoảng thời gian không được vượt quá 365 ngày", ErrorCodes.BadRequest);
        }

        return (fromDate, toDate);
    }

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
    private string GetDashboardCacheKey(string role, Guid? branchId, DateOnly fromDate, DateOnly toDate)
    {
        return $"dashboard_{role}_{branchId}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}";
    }

    #endregion

    #region Dashboard

    /// <summary>
    /// Lấy dashboard cho OWNER (toàn hệ thống)
    /// </summary>
    public async Task<OwnerDashboardDto> GetOwnerDashboardAsync(
        ReportFilterDto filter, Guid currentUserId)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);

        // Cache key
        var cacheKey = GetDashboardCacheKey("OWNER", filter.BranchId, fromDate, toDate);

        // Try get from cache
        if (_cache.TryGetValue(cacheKey, out OwnerDashboardDto? cachedData) && cachedData != null)
        {
            _logger.LogInformation("Dashboard cache HIT for {CacheKey}", cacheKey);
            return cachedData;
        }

        _logger.LogInformation("Dashboard cache MISS for {CacheKey}", cacheKey);

        // Query from DB
        var summary = await _reportRepo.GetDashboardSummaryAsync(fromDate, toDate, filter.BranchId);
        
        // TopBranches: Chỉ hiển thị khi KHÔNG filter theo branch cụ thể
        // Nếu đã filter branch thì không cần top branches (chỉ có 1 branch)
        var topBranches = filter.BranchId.HasValue
            ? new List<TopBranchDto>()
            : await _reportRepo.GetTopBranchesAsync(fromDate, toDate, 5);
            
        var topCustomers = await _reportRepo.GetTopCustomersAsync(fromDate, toDate, filter.BranchId, 5);
        var revenueTrend = await _reportRepo.GetRevenueTrendAsync(fromDate, toDate, filter.BranchId);
        var bookingTrend = await _reportRepo.GetBookingTrendAsync(fromDate, toDate, filter.BranchId);

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
    /// Lấy dashboard cho BRANCH_MANAGER (chỉ chi nhánh mình)
    /// </summary>
    public async Task<ManagerDashboardDto> GetManagerDashboardAsync(
        ReportFilterDto filter, Guid currentUserId)
    {
        var (fromDate, toDate) = ValidateDateRange(filter);

        // Lấy branchId của manager (bắt buộc)
        var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
        if (managerBranch == null)
            throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

        var branchId = managerBranch.BranchId;

        // Cache key
        var cacheKey = GetDashboardCacheKey("MANAGER", branchId, fromDate, toDate);

        // Try get from cache
        if (_cache.TryGetValue(cacheKey, out ManagerDashboardDto? cachedData) && cachedData != null)
        {
            _logger.LogInformation("Dashboard cache HIT for {CacheKey}", cacheKey);
            return cachedData;
        }

        _logger.LogInformation("Dashboard cache MISS for {CacheKey}", cacheKey);

        // Query from DB (chỉ chi nhánh mình)
        var summary = await _reportRepo.GetDashboardSummaryAsync(fromDate, toDate, branchId);
        var topCustomers = await _reportRepo.GetTopCustomersAsync(fromDate, toDate, branchId, 5);
        var revenueTrend = await _reportRepo.GetRevenueTrendAsync(fromDate, toDate, branchId);
        var bookingTrend = await _reportRepo.GetBookingTrendAsync(fromDate, toDate, branchId);

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
}
