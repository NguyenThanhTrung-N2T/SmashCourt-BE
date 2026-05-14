using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CustomerManagement;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Service quản lý khách hàng - chỉ dành cho OWNER và BRANCH_MANAGER
/// </summary>
public interface ICustomerManagementService
{
    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang
    /// OWNER: Xem tất cả khách hàng toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem khách hàng đã từng đặt sân tại chi nhánh mình
    /// </summary>
    Task<PagedResult<CustomerListDto>> GetCustomersAsync(CustomerListQuery query, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng
    /// OWNER: Xem toàn bộ thông tin
    /// BRANCH_MANAGER: Xem thông tin giới hạn (không có email, login info, loyalty transactions)
    /// </summary>
    Task<CustomerDetailDto> GetCustomerByIdAsync(Guid customerId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Lấy lịch sử booking của khách hàng
    /// OWNER: Xem tất cả booking toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem booking tại chi nhánh mình
    /// </summary>
    Task<PagedResult<CustomerBookingDto>> GetCustomerBookingsAsync(Guid customerId, CustomerBookingQuery query, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty (chỉ OWNER)
    /// </summary>
    Task<PagedResult<LoyaltyTransactionDto>> GetLoyaltyTransactionsAsync(Guid customerId, LoyaltyTransactionQuery query, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Lấy thống kê khách hàng
    /// OWNER: Thống kê toàn hệ thống
    /// BRANCH_MANAGER: Thống kê chi nhánh mình
    /// </summary>
    Task<CustomerStatisticsDto> GetCustomerStatisticsAsync(Guid customerId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    Task LockCustomerAsync(Guid customerId, LockCustomerDto dto, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Mở khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    Task UnlockCustomerAsync(Guid customerId, Guid currentUserId, string currentUserRole);
}
