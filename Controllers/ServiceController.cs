using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Service;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;
using System.Linq;

[ApiController]
[Route("api/services")]
[Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
public class ServiceController : ControllerBase
{
    private readonly IServiceService _service;
    private readonly IBranchService _branchService;

    public ServiceController(IServiceService service, IBranchService branchService)
    {
        _service = service;
        _branchService = branchService;
    }

    /// <summary>
    /// Lấy danh sách service đang ACTIVE — có phân trang
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
    {
        var result = await _service.GetAllAsync(query);
        return Ok(ApiResponse<PagedResult<ServiceDto>>.Ok(result, "Lấy danh sách dịch vụ thành công"));
    }


    /// <summary>
    /// Tạo dịch vụ mới — chỉ OWNER
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateServiceDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return StatusCode(201, ApiResponse<ServiceDto>.Ok(result, "Tạo dịch vụ thành công"));
    }

    /// <summary>
    /// Cập nhật dịch vụ — chỉ OWNER
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServiceDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return Ok(ApiResponse<ServiceDto>.Ok(result, "Cập nhật dịch vụ thành công"));
    }

    /// <summary>
    /// Xóa mềm dịch vụ — chỉ OWNER
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok(ApiResponse<object>.Ok(null!, "Xóa dịch vụ thành công"));
    }
    /// <summary>
    /// Lấy danh sách dịch vụ của một chi nhánh (phân trang).
    /// branchId: optional for BRANCH_MANAGER/STAFF (auto-resolve), required for OWNER/CUSTOMER.
    /// </summary>
    [HttpGet("branch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBranchServices([FromQuery] Guid? branchId, [FromQuery] PaginationQuery query)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = userIdStr != null ? Guid.Parse(userIdStr) : Guid.Empty;
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var pagedResult = await _branchService.GetServicesAsync(branchId, query, userId, role);
        return Ok(ApiResponse<PagedResult<BranchServiceDto>>.Ok(pagedResult, "Lấy danh sách dịch vụ thành công"));
    }
    [HttpPost("branch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddServiceToBranch([FromQuery] Guid? branchId, [FromBody] AddServiceToBranchDto dto)
    {
        var currentUserId = Guid.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _branchService.AddServiceAsync(
            branchId, dto, currentUserId, currentUserRole);

        return StatusCode(201,
            ApiResponse<BranchServiceDto>.Ok(result, "Bật dịch vụ thành công"));
    }
    /// <summary>
    /// Cập nhật giá dịch vụ tại chi nhánh — OWNER hoặc MANAGER chi nhánh đó
    /// </summary>
    [HttpPut("branch/{serviceId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateServicePriceInBranch(
        [FromQuery] Guid? branchId, Guid serviceId, [FromBody] UpdateBranchServiceDto dto)
    {
        var currentUserId = Guid.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _branchService.UpdateServicePriceAsync(
            branchId, serviceId, dto, currentUserId, currentUserRole);

        return Ok(ApiResponse<BranchServiceDto>.Ok(result));
    }
    /// <summary>
    /// Tắt dịch vụ khỏi chi nhánh — OWNER hoặc MANAGER chi nhánh đó
    /// </summary>
    [HttpDelete("branch/{serviceId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableBranchService([FromQuery] Guid? branchId, Guid serviceId)
    {
        var currentUserId = Guid.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _branchService.DisableServiceAsync(
            branchId, serviceId, currentUserId, currentUserRole);

        return Ok(ApiResponse<object>.Ok(null!, "Tắt dịch vụ thành công"));
    }
}
