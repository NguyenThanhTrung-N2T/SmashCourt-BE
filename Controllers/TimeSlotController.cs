using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    // Controllers/TimeSlotController.cs
    [ApiController]
    [Route("api/time-slots")]
    public class TimeSlotController : ControllerBase
    {
        private readonly ITimeSlotService _service;

        public TimeSlotController(ITimeSlotService service)
        {
            _service = service;
        }

        /// <summary>
        /// Danh sách tất cả khung giờ — grouped WEEKDAY + WEEKEND
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll()
        {
            var result = await _service.GetAllAsync();
            return Ok(ApiResponse<List<TimeSlotDto>>.Ok(result,"Lấy danh sách khung giờ thành công"));
        }

        /// <summary>
        /// Tạo khung giờ mới — tự động tạo WEEKDAY + WEEKEND
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateTimeSlotDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return StatusCode(201,
                ApiResponse<TimeSlotDto>.Ok(result, "Tạo khung giờ thành công"));
        }

        /// <summary>
        /// Cập nhật khung giờ — cập nhật cả WEEKDAY + WEEKEND
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(Guid id, [FromBody] CreateTimeSlotDto dto)
        {
            var result = await _service.UpdateAsync(id, dto);
            return Ok(ApiResponse<TimeSlotDto>.Ok(result, "Cập nhật khung giờ thành công"));
        }

        /// <summary>
        /// Xóa khung giờ — xóa cả WEEKDAY + WEEKEND
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa khung giờ thành công"));
        }
    }
}
