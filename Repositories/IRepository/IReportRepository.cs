using SmashCourt_BE.DTOs.Report;

namespace SmashCourt_BE.Repositories.IRepository;

/// <summary>
/// Repository interface cho Report & Analytics
/// </summary>
public interface IReportRepository
{
    // Dashboard queries
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);
    Task<List<TopBranchDto>> GetTopBranchesAsync(DateOnly fromDate, DateOnly toDate, int limit);
    Task<List<TopCustomerDto>> GetTopCustomersAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, int limit);
    Task<List<RevenueTrendDto>> GetRevenueTrendAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);
    Task<List<BookingTrendDto>> GetBookingTrendAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);
    
    // Revenue queries
    Task<RevenueReportDto> GetRevenueReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);
    
    // Booking queries
    Task<BookingReportDto> GetBookingReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);
}
