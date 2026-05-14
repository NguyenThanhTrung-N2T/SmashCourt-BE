using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository;

/// <summary>
/// Repository cho Customer Management
/// </summary>
public interface ICustomerManagementRepository
{
    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang
    /// OWNER: Xem tất cả khách hàng toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem khách hàng đã từng đặt sân tại chi nhánh mình
    /// </summary>
    Task<PagedResult<User>> GetCustomersAsync(CustomerListQuery query, Guid? managerBranchId);

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng
    /// </summary>
    Task<User?> GetCustomerByIdAsync(Guid customerId);

    /// <summary>
    /// Kiểm tra khách hàng có booking tại chi nhánh không (dùng cho BRANCH_MANAGER)
    /// </summary>
    Task<bool> HasBookingAtBranchAsync(Guid customerId, Guid branchId);

    /// <summary>
    /// Lấy lịch sử booking của khách hàng
    /// OWNER: Xem tất cả booking toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem booking tại chi nhánh mình
    /// </summary>
    Task<PagedResult<Booking>> GetCustomerBookingsAsync(Guid customerId, CustomerBookingQuery query, Guid? managerBranchId);

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty (chỉ OWNER)
    /// </summary>
    Task<PagedResult<LoyaltyTransaction>> GetLoyaltyTransactionsAsync(Guid customerId, LoyaltyTransactionQuery query);

    /// <summary>
    /// Lấy thống kê khách hàng
    /// OWNER: Thống kê toàn hệ thống
    /// BRANCH_MANAGER: Thống kê chi nhánh mình
    /// </summary>
    Task<CustomerStatisticsDto> GetCustomerStatisticsAsync(Guid customerId, Guid? managerBranchId);

    /// <summary>
    /// Lấy số lượng booking COMPLETED của nhiều customers (batch query - tránh N+1)
    /// </summary>
    Task<Dictionary<Guid, int>> GetCompletedBookingCountBatchAsync(List<Guid> customerIds, Guid? managerBranchId);
}
