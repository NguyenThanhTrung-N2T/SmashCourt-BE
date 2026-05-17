using SmashCourt_BE.DTOs.Report;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Service interface cho Report & Analytics
/// </summary>
public interface IReportService
{
    // Dashboard
    // OWNER: Dashboard tổng quan toàn hệ thống
    Task<OwnerDashboardDto> GetOwnerDashboardAsync(ReportFilterDto filter, Guid currentUserId);

    // MANAGER: Dashboard tổng quan chi nhánh
    Task<ManagerDashboardDto> GetManagerDashboardAsync(ReportFilterDto filter, Guid currentUserId);

    // Lấy báo cáo doanh thu theo filter
    Task<RevenueReportDto> GetRevenueReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);

    // lấy báo cáo booking theo filter
    Task<BookingReportDto> GetBookingReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);

    // lấy báo cáo sử dụng sân theo filter
    Task<CourtUtilizationReportDto> GetCourtUtilizationReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);

    // lấy báo cáo thống kê khách hàng theo filter
    Task<CustomerStatisticsReportDto> GetCustomerStatisticsReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);

    // lấy top 10 khách hàng chi tiêu nhiều nhất theo filter
    Task<TopSpendersReportDto> GetTopSpendersReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole, int page, int pageSize);

    // lấy báo cáo hiệu suất dịch vụ theo filter
    Task<ServicePerformanceReportDto> GetServicePerformanceReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);

    // lấy báo cáo hiệu quả khuyến mãi theo filter
    Task<PromotionEffectivenessReportDto> GetPromotionEffectivenessReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);
}
