using SmashCourt_BE.DTOs.Report;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Service interface cho Report & Analytics
/// </summary>
public interface IReportService
{
    // Dashboard
    Task<OwnerDashboardDto> GetOwnerDashboardAsync(ReportFilterDto filter, Guid currentUserId);
    Task<ManagerDashboardDto> GetManagerDashboardAsync(ReportFilterDto filter, Guid currentUserId);
    
    // Revenue
    Task<RevenueReportDto> GetRevenueReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);
    
    // Booking
    Task<BookingReportDto> GetBookingReportAsync(ReportFilterDto filter, Guid currentUserId, string currentUserRole);
}
