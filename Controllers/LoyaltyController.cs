using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;
using SmashCourt_BE.Common;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/loyalty")]
    [Authorize]
    public class LoyaltyController : ControllerBase
    {
        private readonly ILoyaltyService _service;

        public LoyaltyController(ILoyaltyService service)
        {
            _service = service;
        }

        /// <summary>
        /// Xem thông tin loyalty của bản thân
        /// </summary>
        [HttpGet("me")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMyLoyalty()
        {
            // Lấy userId từ JWT claim
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _service.GetMyLoyaltyAsync(userId);
            return Ok(result);
        }


        /// <summary>
        /// Xem lịch sử tích điểm của bản thân
        /// </summary>
        [HttpGet("me/transactions")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetMyTransactions(
            [FromQuery] PaginationQuery query)
        {
            if (!ModelState.IsValid)
                return BadRequest("Thông tin không hợp lệ");

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _service.GetMyTransactionsAsync(userId, query);
            return Ok(result);
        }

    }
}
