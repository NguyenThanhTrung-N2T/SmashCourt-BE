using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Service;
using SmashCourt_BE.Services.IService;

[ApiController]
[Route("api/services")]
[Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
public class ServiceController : ControllerBase
{
    private readonly IServiceService _service;

    public ServiceController(IServiceService service)
    {
        _service = service;
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
}
