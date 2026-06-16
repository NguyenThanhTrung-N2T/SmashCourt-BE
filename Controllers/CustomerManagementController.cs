using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Controller quản lý khách hàng và tìm nhanh khách hàng cho nhân sự.
/// </summary>
[ApiController]
[Route("api/customers")]
public class CustomerManagementController : ControllerBase
{
    private readonly ICustomerManagementService _service;

    public CustomerManagementController(ICustomerManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang.
    /// OWNER xem toàn hệ thống; BRANCH_MANAGER chỉ xem khách từng đặt sân tại chi nhánh của mình.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCustomers([FromQuery] CustomerListQuery query)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.GetCustomersAsync(query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<CustomerListDto>>.Ok(result, "Lấy danh sách khách hàng thành công"));
    }

    /// <summary>
    /// Tìm nhanh khách hàng theo tên, email hoặc số điện thoại.
    /// OWNER tìm toàn hệ thống; BRANCH_MANAGER và STAFF chỉ tìm trong chi nhánh đang được gán.
    /// </summary>
    [HttpGet("search")]
    [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchCustomers([FromQuery] CustomerSearchQuery query)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.SearchCustomersAsync(query, currentUserId, currentUserRole);
        return Ok(ApiResponse<List<CustomerSearchDto>>.Ok(result, "Tìm kiếm khách hàng thành công"));
    }

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng.
    /// OWNER xem đầy đủ; BRANCH_MANAGER xem theo phạm vi chi nhánh.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerById(Guid id)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.GetCustomerByIdAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<CustomerDetailDto>.Ok(result, "Lấy thông tin khách hàng thành công"));
    }

    /// <summary>
    /// Lấy lịch sử booking của khách hàng.
    /// </summary>
    [HttpGet("{id:guid}/bookings")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerBookings(Guid id, [FromQuery] CustomerBookingQuery query)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.GetCustomerBookingsAsync(id, query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<CustomerBookingDto>>.Ok(result, "Lấy lịch sử booking thành công"));
    }

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty của khách hàng. Chỉ OWNER được truy cập.
    /// </summary>
    [HttpGet("{id:guid}/loyalty-transactions")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLoyaltyTransactions(Guid id, [FromQuery] LoyaltyTransactionQuery query)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.GetLoyaltyTransactionsAsync(id, query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<LoyaltyTransactionDto>>.Ok(result, "Lấy lịch sử tích điểm thành công"));
    }

    /// <summary>
    /// Lấy thống kê của khách hàng.
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerStatistics(Guid id)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        var result = await _service.GetCustomerStatisticsAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<CustomerStatisticsDto>.Ok(result, "Lấy thống kê khách hàng thành công"));
    }

    /// <summary>
    /// Khóa tài khoản khách hàng. Chỉ OWNER được thao tác.
    /// </summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LockCustomer(Guid id, [FromBody] LockCustomerDto dto)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        await _service.LockCustomerAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Khóa tài khoản khách hàng thành công"));
    }

    /// <summary>
    /// Mở khóa tài khoản khách hàng. Chỉ OWNER được thao tác.
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockCustomer(Guid id)
    {
        var (currentUserId, currentUserRole) = GetCurrentUser();
        await _service.UnlockCustomerAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Mở khóa tài khoản khách hàng thành công"));
    }

    private (Guid UserId, string Role) GetCurrentUser()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        return (userId, role);
    }
}
