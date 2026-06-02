using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/courts")]
    public class CourtController : ControllerBase
    {
        private readonly ICourtService _service;

        public CourtController(ICourtService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy danh sách sân.
        /// Public: cần branchId.
        /// Internal: auto-resolve branchId nếu không truyền.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? typeId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = userIdStr != null ? Guid.Parse(userIdStr) : (Guid?)null;
            var role = User.FindFirstValue(ClaimTypes.Role);

            var result = await _service.GetAllAsync(branchId, typeId, userId, role);
            return Ok(ApiResponse<List<CourtDto>>.Ok(result));
        }

        /// <summary>
        /// Xem chi tiết 1 sân
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? branchId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userId = userIdStr != null ? Guid.Parse(userIdStr) : (Guid?)null;
            var role = User.FindFirstValue(ClaimTypes.Role);

            var result = await _service.GetByIdAsync(id, branchId, userId, role);
            return Ok(ApiResponse<CourtDto>.Ok(result));
        }

        /// <summary>
        /// Stats-only dashboard — 4 ô thống kê, có thể poll độc lập mọi 30–60 giây. (todo: signalr)
        /// </summary>
        [HttpGet("management-dashboard/stats")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetManagementStats(
            [FromQuery] Guid? branchId,
            [FromQuery] DateOnly? date)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetManagementStatsAsync(branchId, date, userId, role);
            return Ok(ApiResponse<CourtManagementStatsDto>.Ok(result, "Lấy thống kê sân thành công"));
        }

        /// <summary>
        /// Danh sách card sân (phân trang) kèm timeline ngày được chọn.
        /// </summary>
        [HttpGet("management-dashboard/courts")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetManagementCourts(
            [FromQuery] CourtManagementDashboardQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetManagementCourtsAsync(
                query.BranchId, query.Date, query.Search, query.TypeId,
                query.Page, query.PageSize, userId, role);

            return Ok(ApiResponse<Common.PagedResult<CourtManagementCardDto>>.Ok(result, "Lấy danh sách card sân thành công"));
        }

        /// <summary>
        /// Full-detail timeline — tất cả sân trong ngày kèm thông tin đặt sân. (todo: signalr)
        /// </summary>
        [HttpGet("management-timeline")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetManagementTimeline(
            [FromQuery] Guid? branchId,
            [FromQuery] DateOnly date,
            [FromQuery] Guid? typeId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetManagementTimelineAsync(branchId, date, typeId, userId, role);
            return Ok(ApiResponse<CourtManagementTimelineDto>.Ok(result, "Lấy timeline quản lý sân thành công"));
        }

        /// <summary>
        /// Chi tiết sân cho modal quản lý — giá, khách đang chơi, lịch sắp tới.
        /// </summary>
        [HttpGet("{id:guid}/management-details")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetManagementDetail(Guid id, [FromQuery] DateOnly? date)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.GetManagementDetailAsync(id, date, userId, role);
            return Ok(ApiResponse<CourtManagementDetailDto>.Ok(result, "Lấy chi tiết sân thành công"));
        }

        /// <summary>
        /// Thêm sân mới.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromQuery] Guid? branchId, [FromBody] CreateCourtDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.CreateAsync(branchId, dto, userId, role);
            return StatusCode(201, ApiResponse<CourtDto>.Ok(result, "Tạo sân thành công"));
        }

        /// <summary>
        /// Cập nhật thông tin sân.
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(Guid id, [FromQuery] Guid? branchId, [FromBody] UpdateCourtDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.UpdateAsync(id, branchId, dto, userId, role);
            return Ok(ApiResponse<CourtDto>.Ok(result, "Cập nhật sân thành công"));
        }

        /// <summary>
        /// Tạm ngưng sân.
        /// </summary>
        [HttpPost("{id:guid}/suspend")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Suspend(Guid id, [FromQuery] Guid? branchId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.SuspendAsync(id, branchId, userId, role);
            return Ok(ApiResponse<object>.Ok(null!, "Tạm ngưng sân thành công"));
        }

        /// <summary>
        /// Mở lại sân.
        /// </summary>
        [HttpPost("{id:guid}/activate")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Activate(Guid id, [FromQuery] Guid? branchId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.ActivateAsync(id, branchId, userId, role);
            return Ok(ApiResponse<object>.Ok(null!, "Mở lại sân thành công"));
        }

        /// <summary>
        /// Xóa mềm sân.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? branchId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.DeleteAsync(id, branchId, userId, role);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa sân thành công"));
        }
    }
}
