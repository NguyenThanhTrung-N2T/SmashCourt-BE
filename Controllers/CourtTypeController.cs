using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Services.IService;

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
        return Ok(ApiResponse<object>.Ok(null!,"Xóa loại sân thành công"));
    }
}