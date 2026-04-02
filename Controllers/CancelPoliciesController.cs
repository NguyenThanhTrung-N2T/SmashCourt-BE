using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CancelPolicy;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers;

[Route("api/cancel-policies")]
[ApiController]
[Authorize]
public class CancelPoliciesController : ControllerBase
{
    private readonly ICancelPolicyService _cancelPolicyService;

    public CancelPoliciesController(ICancelPolicyService cancelPolicyService)
    {
        _cancelPolicyService = cancelPolicyService;
    }

    /// <summary>
    /// Lấy tất cả chính sách hủy —
    /// cho phép: mọi user đã xác thực
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll()
    {
        var policies = await _cancelPolicyService.GetAllPolicesAsync();
        return Ok(ApiResponse<IEnumerable<CancelPolicyDto>>.Ok(policies, "Lấy danh sách chính sách hủy thành công"));
    }

    /// <summary>
    /// Tạo mới chính sách hủy — chỉ OWNER
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateCancelPolicyDto dto)
    {
        var result = await _cancelPolicyService.CreatePolicyAsync(dto);
        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<CancelPolicyDto>.Ok(result, "Tạo chính sách hủy thành công"));
    }

    /// <summary>
    /// Cập nhật chính sách hủy — chỉ OWNER
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCancelPolicyDto dto)
    {
        var result = await _cancelPolicyService.UpdatePolicyAsync(id, dto);
        return Ok(ApiResponse<CancelPolicyDto>.Ok(result, "Cập nhật chính sách hủy thành công"));
    }

    /// <summary>
    /// Xóa chính sách hủy — chỉ OWNER
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _cancelPolicyService.DeletePolicyAsync(id);
        return NoContent();
    }
}
