using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api/court-types")]
[Authorize]
public class CourtTypeController : ControllerBase
{
    private readonly ICourtTypeService _service;

    public CourtTypeController(ICourtTypeService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách loại sân đang ACTIVE — có phân trang.
    /// Cho phép: mọi user đã xác thực
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
    {
        var result = await _service.GetAllCourtTypesAsync(query);
        return Ok(ApiResponse<PagedResult<CourtTypeDto>>.Ok(result, "Lấy danh sách loại sân thành công"));
    }

    /// <summary>
    /// Xem chi tiết 1 loại sân.
    /// Cho phép: mọi user đã xác thực
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(ApiResponse<CourtTypeDto>.Ok(result, "Lấy chi tiết loại sân thành công"));
    }

    /// <summary>
    /// Tạo loại sân mới — chỉ OWNER
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateCourtTypeDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<CourtTypeDto>.Ok(result, "Tạo loại sân thành công"));
    }

    /// <summary>
    /// Cập nhật loại sân — chỉ OWNER
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCourtTypeDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return Ok(ApiResponse<CourtTypeDto>.Ok(result, "Cập nhật loại sân thành công"));
    }

    /// <summary>
    /// Xóa mềm loại sân — chỉ OWNER
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok(ApiResponse<object>.Ok(null!, "Xóa loại sân thành công"));
    }

    /// <summary>
    /// Lấy danh sách loại sân tại chi nhánh (Auto-resolve branchId cho Manager/Staff)
    /// </summary>
    [HttpGet("branch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBranch([FromQuery] Guid? branchId)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetCourtTypesAsync(branchId, currentUserId, currentUserRole);
        return Ok(ApiResponse<List<BranchCourtTypeDto>>.Ok(result, "Lấy danh sách loại sân chi nhánh thành công"));
    }

    /// <summary>
    /// Bật loại sân cho chi nhánh — OWNER hoặc MANAGER chi nhánh đó
    /// </summary>
    [HttpPost("branch")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddToBranch([FromQuery] Guid? branchId, [FromBody] AddCourtTypeToBranchDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.AddCourtTypeAsync(branchId, dto, currentUserId, currentUserRole);
        return StatusCode(201, ApiResponse<BranchCourtTypeDto>.Ok(result, "Bật loại sân thành công"));
    }

    /// <summary>
    /// Tắt loại sân khỏi chi nhánh — OWNER hoặc MANAGER chi nhánh đó
    /// </summary>
    [HttpDelete("branch/{courtTypeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFromBranch(Guid courtTypeId, [FromQuery] Guid? branchId)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.RemoveCourtTypeAsync(branchId, courtTypeId, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Tắt loại sân thành công"));
    }
}