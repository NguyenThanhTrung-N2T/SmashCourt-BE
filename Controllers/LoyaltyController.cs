using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Loyalty;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api/loyalty")]
[Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
public class LoyaltyController : ControllerBase
{
    private readonly ILoyaltyService _service;

    public LoyaltyController(ILoyaltyService service)
    {
        _service = service;
    }

    /// <summary>
    /// Xem thông tin loyalty của bản thân — chỉ CUSTOMER
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyLoyalty()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _service.GetMyLoyaltyAsync(userId);
        return Ok(ApiResponse<MyLoyaltyDto>.Ok(result, "Lấy thông tin loyalty thành công"));
    }

    /// <summary>
    /// Xem lịch sử tích điểm của bản thân — chỉ CUSTOMER
    /// </summary>
    [HttpGet("me/transactions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyTransactions([FromQuery] PaginationQuery query)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var result = await _service.GetMyTransactionsAsync(userId, query);
        return Ok(ApiResponse<PagedResult<LoyaltyTransactionDto>>.Ok(result, "Lấy lịch sử tích điểm thành công"));
    }
}
