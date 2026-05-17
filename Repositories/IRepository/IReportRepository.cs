using SmashCourt_BE.DTOs.Report;

namespace SmashCourt_BE.Repositories.IRepository;

/// <summary>
/// Repository interface cho Report & Analytics
/// </summary>
public interface IReportRepository
{
    // Lấy dữ liệu tổng quan cho dashboard
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);

    // Lấy top chi nhánh, khách hàng, xu hướng doanh thu, xu hướng đặt sân
    Task<List<TopBranchDto>> GetTopBranchesAsync(DateOnly fromDate, DateOnly toDate, int limit);
    Task<List<TopCustomerDto>> GetTopCustomersAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, int limit);
    Task<List<RevenueTrendDto>> GetRevenueTrendAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);
    Task<List<BookingTrendDto>> GetBookingTrendAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId);

    // lấy báo cáo doanh thu
    Task<RevenueReportDto> GetRevenueReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);

    // lấy báo cáo booking
    Task<BookingReportDto> GetBookingReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);

    // lấy báo cáo sử dụng sân
    Task<CourtUtilizationReportDto> GetCourtUtilizationReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);

    // lấy báo cáo thống kê khách hàng
    Task<CustomerStatisticsReportDto> GetCustomerStatisticsReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);

    // lấy báo cáo top khách hàng chi tiêu nhiều nhất
    Task<TopSpendersReportDto> GetTopSpendersReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, int page, int pageSize);

    // báo cáo hiệu suất dịch vụ (doanh thu dịch vụ, tỉ lệ gắn dịch vụ, doanh thu trung bình trên mỗi booking có dịch vụ, top dịch vụ, xu hướng sử dụng dịch vụ)
    Task<ServicePerformanceReportDto> GetServicePerformanceReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);

    // lấy báo cáo hiệu quả khuyến mãi (doanh thu từ khuyến mãi, tỉ lệ sử dụng khuyến mãi, doanh thu trung bình trên mỗi booking có khuyến mãi, top khuyến mãi, xu hướng sử dụng khuyến mãi)
    Task<PromotionEffectivenessReportDto> GetPromotionEffectivenessReportAsync(DateOnly fromDate, DateOnly toDate, Guid? branchId, string? groupBy);
}
