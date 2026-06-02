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
    [ApiController]
    [Route("api/branches/prices")]
    public class BranchPriceController : ControllerBase
    {
        private readonly IBranchPriceService _service;

        public BranchPriceController(IBranchPriceService service)
        {
            _service = service;
        }
        /// <summary>
        /// Giá thực tế đang áp dụng tại chi nhánh (branch override nếu có, fallback về system price)
        /// </summary>
        [HttpGet("current")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCurrent(
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? courtTypeId = null)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetEffectiveCurrentAsync(branchId, courtTypeId, userId, role);
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
            [FromQuery] Guid? branchId,
            [FromQuery] DateTime? date,
            [FromQuery] Guid? courtTypeId = null)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            if (date == null)
                return BadRequest(ApiResponse<object>.Fail(
                    "Vui lòng đưa ngày cần xem.",
                    ErrorCodes.BadRequest));

            var parsedDate = DateOnly.FromDateTime(date.Value);
            var result = await _service.GetEffectiveResolvedAsync(branchId, parsedDate, courtTypeId, userId, role);
            return Ok(ApiResponse<List<EffectivePriceDto>>.Ok(result));
        }

        /// <summary>
        /// List branch override price versions by effective date.
        /// </summary>
        [HttpGet("versions")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetVersions(
            [FromQuery] Guid? branchId,
            [FromQuery] Guid courtTypeId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetVersionsAsync(branchId, courtTypeId, userId, role);
            return Ok(ApiResponse<List<PriceVersionListDto>>.Ok(result));
        }

        /// <summary>
        /// Lấy chi tiết một phiên bản giá chi nhánh theo ngày hiệu lực.
        /// </summary>
        [HttpGet("version")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVersionDetail(
            [FromQuery] Guid? branchId,
            [FromQuery] Guid courtTypeId,
            [FromQuery] DateTime? effectiveFrom)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            if (effectiveFrom == null)
                return BadRequest(ApiResponse<object>.Fail(
                    "Vui lòng đưa ngày hiệu lực.",
                    ErrorCodes.BadRequest));

            var effectiveFromDate = DateOnly.FromDateTime(effectiveFrom.Value);
            var result = await _service.GetVersionDetailAsync(branchId, courtTypeId, effectiveFromDate, userId, role);
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
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create(
            [FromQuery] Guid? branchId,
            [FromBody] CreateBranchPriceDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.CreateBatchAsync(branchId, dto, userId, role);
            return StatusCode(201,
                ApiResponse<object>.Ok(null!, "Cấu hình giá chi nhánh thành công"));
        }

        /// <summary>
        /// Xóa cấu hình giá override — fallback về system price
        /// </summary>
        [HttpDelete]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(
            [FromQuery] Guid? branchId,
            [FromBody] DeleteBranchPriceDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.DeleteAsync(branchId, dto, userId, role);
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
            [FromQuery] Guid branchId,
            [FromBody] CalculatePriceDto dto)
        {
            var result = await _service.CalculateAsync(branchId, dto);
            return Ok(ApiResponse<CalculatePriceResultDto>.Ok(result));
        }
    }
}
