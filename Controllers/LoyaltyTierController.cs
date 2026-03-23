using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.DTOs.LoyaltyTier;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
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
        /// Lấy danh sách tất cả hạng thành viên
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllLoyaltyTiers()
        {
            var result = await _service.GetAllLoyaltyTiersAsync();
            return Ok(result);
        }

        /// <summary>
        /// Xem chi tiết 1 hạng thành viên
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetLoyaltyTierById(Guid id)
        {
            var result = await _service.GetLoyaltyTierByIdAsync(id);
            return Ok(result);
        }

        /// <summary>
        /// Cập nhật hạng thành viên — chỉ OWNER
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "OWNER")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLoyaltyTierDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest("Thông tin không hợp lệ");

            var result = await _service.UpdateLoyaltyTierAsync(id, dto);
            return Ok(result);
        }


    }
}
