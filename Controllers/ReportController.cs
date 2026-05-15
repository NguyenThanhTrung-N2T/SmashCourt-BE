using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Controller cho Report & Analytics
/// Chỉ dành cho OWNER và BRANCH_MANAGER
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
public class ReportController : ControllerBase
{
    private readonly IReportService _service;

    public ReportController(IReportService service)
    {
        _service = service;
    }

    /// <summary>
    /// Dashboard cho OWNER (toàn hệ thống)
    /// </summary>
    [HttpGet("dashboard/owner")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOwnerDashboard([FromQuery] ReportFilterDto filter)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _service.GetOwnerDashboardAsync(filter, currentUserId);
        return Ok(ApiResponse<OwnerDashboardDto>.Ok(result, "Lấy dashboard thành công"));
    }

    /// <summary>
    /// Dashboard cho BRANCH_MANAGER (chỉ chi nhánh mình)
    /// </summary>
    [HttpGet("dashboard/manager")]
    [Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetManagerDashboard([FromQuery] ReportFilterDto filter)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _service.GetManagerDashboardAsync(filter, currentUserId);
        return Ok(ApiResponse<ManagerDashboardDto>.Ok(result, "Lấy dashboard thành công"));
    }

    /// <summary>
    /// Báo cáo doanh thu
    /// OWNER: Xem toàn hệ thống hoặc filter theo branch
    /// BRANCH_MANAGER: Chỉ xem chi nhánh mình
    /// </summary>
    [HttpGet("revenue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueReport([FromQuery] ReportFilterDto filter)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;
        
        var result = await _service.GetRevenueReportAsync(filter, currentUserId, currentUserRole);
        return Ok(ApiResponse<RevenueReportDto>.Ok(result, "Lấy báo cáo doanh thu thành công"));
    }

    /// <summary>
    /// Báo cáo booking
    /// OWNER: Xem toàn hệ thống hoặc filter theo branch
    /// BRANCH_MANAGER: Chỉ xem chi nhánh mình
    /// </summary>
    [HttpGet("bookings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBookingReport([FromQuery] ReportFilterDto filter)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;
        
        var result = await _service.GetBookingReportAsync(filter, currentUserId, currentUserRole);
        return Ok(ApiResponse<BookingReportDto>.Ok(result, "Lấy báo cáo booking thành công"));
    }
}
