using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/system-prices")]
    public class SystemPriceController : ControllerBase
    {
        private readonly ISystemPriceService _service;

        public SystemPriceController(ISystemPriceService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lịch sử toàn bộ giá chung — filter theo court type nếu có
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] Guid? courtTypeId = null)
        {
            var result = await _service.GetAllAsync(courtTypeId);
            return Ok(ApiResponse<List<CurrentPriceDto>>.Ok(result));
        }

        /// <summary>
        /// Giá chung đang áp dụng hiện tại
        /// </summary>
        [HttpGet("current")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCurrent([FromQuery] Guid? courtTypeId = null)
        {
            var result = await _service.GetCurrentAsync(courtTypeId);
            return Ok(ApiResponse<List<CurrentPriceDto>>.Ok(result));
        }

        /// <summary>
        /// Tạo cấu hình giá mới — insert batch WEEKDAY + WEEKEND
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateSystemPriceDto dto)
        {
            await _service.CreateBatchAsync(dto);
            return StatusCode(201,
                ApiResponse<object>.Ok(null!, "Cấu hình giá thành công"));
        }
    }
}
