using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
    // Controllers/BranchPriceController.cs
    [ApiController]
    [Route("api/branches/{branchId:guid}/prices")]
    public class BranchPriceController : ControllerBase
    {
        private readonly IBranchPriceService _service;

        public BranchPriceController(IBranchPriceService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lịch sử toàn bộ giá override tại chi nhánh
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(
            Guid branchId, [FromQuery] Guid? courtTypeId = null)
        {
            var result = await _service.GetAllAsync(branchId, courtTypeId);
            return Ok(ApiResponse<List<CurrentPriceDto>>.Ok(result));
        }

        /// <summary>
        /// Giá thực tế đang áp dụng tại chi nhánh (branch override nếu có, fallback về system price)
        /// </summary>
        [HttpGet("current")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCurrent(
            Guid branchId, [FromQuery] Guid? courtTypeId = null)
        {
            var result = await _service.GetEffectiveCurrentAsync(branchId, courtTypeId);
            return Ok(ApiResponse<List<EffectivePriceDto>>.Ok(result));
        }

        /// <summary>
        /// Lấy snapshot giá thực tế tại chi nhánh theo một ngày cụ thể.
        /// </summary>
        [HttpGet("resolved")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetResolved(
            Guid branchId,
            [FromQuery] DateTime? date,
            [FromQuery] Guid? courtTypeId = null)
        {
            if (date == null)
                return BadRequest(ApiResponse<object>.Fail(
                    "Vui lòng đưa ngày cần xem.",
                    ErrorCodes.BadRequest));

            var parsedDate = DateOnly.FromDateTime(date.Value);
            var result = await _service.GetEffectiveResolvedAsync(branchId, parsedDate, courtTypeId);
            return Ok(ApiResponse<List<EffectivePriceDto>>.Ok(result));
        }

        /// <summary>
        /// Lấy chi tiết một phiên bản giá chi nhánh theo ngày hiệu lực.
        /// </summary>
        [HttpGet("version")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVersionDetail(
            Guid branchId,
            [FromQuery] Guid courtTypeId,
            [FromQuery] DateTime? effectiveFrom)
        {
            if (effectiveFrom == null)
                return BadRequest(ApiResponse<object>.Fail(
                    "Vui lòng đưa ngày hiệu lực.",
                    ErrorCodes.BadRequest));

            var effectiveFromDate = DateOnly.FromDateTime(effectiveFrom.Value);
            var result = await _service.GetVersionDetailAsync(branchId, courtTypeId, effectiveFromDate);
            if (result == null)
            {
                return Ok(ApiResponse<object>.Fail(
                    "Không tìm thấy cấu hình giá",
                    "PRICE_CONFIG_NOT_FOUND"));
            }

            return Ok(ApiResponse<BranchPriceVersionDetailDto>.Ok(result));
        }

        /// <summary>
        /// Tạo giá override mới — batch WEEKDAY + WEEKEND
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create(
            Guid branchId, [FromBody] CreateBranchPriceDto dto)
        {
            await _service.CreateBatchAsync(branchId, dto);
            return StatusCode(201,
                ApiResponse<object>.Ok(null!, "Cấu hình giá chi nhánh thành công"));
        }

        /// <summary>
        /// Xóa cấu hình giá override — fallback về system price
        /// </summary>
        [HttpDelete]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid branchId, [FromBody] DeleteBranchPriceDto dto)
        {
            await _service.DeleteAsync(branchId, dto);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa cấu hình giá thành công"));
        }

        /// <summary>
        /// Tính giá theo slot khách chọn
        /// </summary>
        [HttpPost("calculate")]
        [AllowAnonymous] // Public — khách tính giá trước khi đặt sân
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Calculate(
            Guid branchId, [FromBody] CalculatePriceDto dto)
        {
            var result = await _service.CalculateAsync(branchId, dto);
            return Ok(ApiResponse<CalculatePriceResultDto>.Ok(result));
        }
    }
}
