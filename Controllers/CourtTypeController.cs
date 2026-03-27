using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Services.Interfaces;

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
    /// Lấy danh sách loại sân đang ACTIVE — có phân trang
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
    {
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        var result = await _service.GetAllCourtTypesAsync(query);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết 1 loại sân
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// Tạo loại sân mới — chỉ OWNER
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCourtTypeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        var result = await _service.CreateAsync(dto);
        return StatusCode(201, result);
    }

    /// <summary>
    /// Cập nhật loại sân — chỉ OWNER
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCourtTypeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        var result = await _service.UpdateAsync(id, dto);
        return Ok(result);
    }

    /// <summary>
    /// Xóa mềm loại sân — chỉ OWNER
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}