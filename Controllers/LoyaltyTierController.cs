using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.LoyaltyTier;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api/loyalty-tiers")]
[Authorize]
public class LoyaltyTierController : ControllerBase
{
    private readonly ILoyaltyTierService _service;

    public LoyaltyTierController(ILoyaltyTierService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách tất cả hạng thành viên — mọi user đã xác thực
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllLoyaltyTiers()
    {
        var result = await _service.GetAllLoyaltyTiersAsync();
        return Ok(ApiResponse<IEnumerable<LoyaltyTierDto>>.Ok(result, "Lấy danh sách hạng thành viên thành công"));
    }

    /// <summary>
    /// Xem chi tiết 1 hạng thành viên — mọi user đã xác thực
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLoyaltyTierById(Guid id)
    {
        var result = await _service.GetLoyaltyTierByIdAsync(id);
        return Ok(ApiResponse<LoyaltyTierDto>.Ok(result));
    }

    /// <summary>
    /// Cập nhật hạng thành viên — chỉ OWNER
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLoyaltyTierDto dto)
    {
        var result = await _service.UpdateLoyaltyTierAsync(id, dto);
        return Ok(ApiResponse<LoyaltyTierDto>.Ok(result, "Cập nhật hạng thành viên thành công"));
    }
}
