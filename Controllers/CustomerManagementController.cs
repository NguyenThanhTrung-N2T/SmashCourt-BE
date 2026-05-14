using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Controller quản lý khách hàng
/// Chỉ dành cho OWNER và BRANCH_MANAGER
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
public class CustomerManagementController : ControllerBase
{
    private readonly ICustomerManagementService _service;

    public CustomerManagementController(ICustomerManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách khách hàng với filter và phân trang
    /// OWNER: Xem tất cả khách hàng toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem khách hàng đã từng đặt sân tại chi nhánh mình
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCustomers([FromQuery] CustomerListQuery query)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetCustomersAsync(query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<CustomerListDto>>.Ok(result, "Lấy danh sách khách hàng thành công"));
    }

    /// <summary>
    /// Lấy thông tin chi tiết khách hàng
    /// OWNER: Xem toàn bộ thông tin
    /// BRANCH_MANAGER: Xem thông tin giới hạn (không có email, login info, loyalty transactions)
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerById(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetCustomerByIdAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<CustomerDetailDto>.Ok(result, "Lấy thông tin khách hàng thành công"));
    }

    /// <summary>
    /// Lấy lịch sử booking của khách hàng
    /// OWNER: Xem tất cả booking toàn hệ thống
    /// BRANCH_MANAGER: Chỉ xem booking tại chi nhánh mình
    /// </summary>
    [HttpGet("{id:guid}/bookings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerBookings(Guid id, [FromQuery] CustomerBookingQuery query)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetCustomerBookingsAsync(id, query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<CustomerBookingDto>>.Ok(result, "Lấy lịch sử booking thành công"));
    }

    /// <summary>
    /// Lấy lịch sử tích điểm loyalty (chỉ OWNER)
    /// </summary>
    [HttpGet("{id:guid}/loyalty-transactions")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLoyaltyTransactions(Guid id, [FromQuery] LoyaltyTransactionQuery query)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetLoyaltyTransactionsAsync(id, query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<LoyaltyTransactionDto>>.Ok(result, "Lấy lịch sử tích điểm thành công"));
    }

    /// <summary>
    /// Lấy thống kê khách hàng
    /// OWNER: Thống kê toàn hệ thống
    /// BRANCH_MANAGER: Thống kê chi nhánh mình
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerStatistics(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetCustomerStatisticsAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<CustomerStatisticsDto>.Ok(result, "Lấy thống kê khách hàng thành công"));
    }

    /// <summary>
    /// Khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LockCustomer(Guid id, [FromBody] LockCustomerDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.LockCustomerAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Khóa tài khoản khách hàng thành công"));
    }

    /// <summary>
    /// Mở khóa tài khoản khách hàng (chỉ OWNER)
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockCustomer(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.UnlockCustomerAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Mở khóa tài khoản khách hàng thành công"));
    }
}
