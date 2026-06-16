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
[Route("api/dashboard")]
[Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
public class DashboardController : ControllerBase
{
    private readonly IReportService _service;
    public DashboardController(IReportService service)
    {
        _service = service;
    }
    /// <summary>
    /// Dashboard cho OWNER (toàn hệ thống)
    /// </summary>
    [HttpGet("owner")]
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
    /// Operational dashboard cho BRANCH_MANAGER & STAFF (chỉ chi nhánh mình)
    /// Khong phai analytics nen STAFF duoc phep access
    /// </summary>
    [HttpGet("branch")]
    [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperationalManagerDashboard()
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;
        var result = await _service.GetOperationalManagerDashboardAsync(currentUserId, currentUserRole);
        return Ok(ApiResponse<OperationalManagerDashboardDto>.Ok(result, "Lấy dashboard thành công"));
    }
}