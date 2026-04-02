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
    // Controllers/CourtController.cs
    [ApiController]
    [Route("api/branches/{branchId:guid}/courts")]
    public class CourtController : ControllerBase
    {
        private readonly ICourtService _service;

        public CourtController(ICourtService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy danh sách sân tại chi nhánh
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAll(Guid branchId)
        {
            var isStaffOrAbove = User.Identity?.IsAuthenticated == true &&
                (User.IsInRole(UserRole.OWNER.ToString()) ||
                 User.IsInRole(UserRole.BRANCH_MANAGER.ToString()) ||
                 User.IsInRole(UserRole.STAFF.ToString()));

            var result = await _service.GetAllByBranchAsync(branchId, isStaffOrAbove);
            return Ok(ApiResponse<List<CourtDto>>.Ok(result));
        }

        /// <summary>
        /// Xem chi tiết 1 sân
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid branchId, Guid id)
        {
            var isStaffOrAbove = User.Identity?.IsAuthenticated == true &&
                (User.IsInRole(UserRole.OWNER.ToString()) ||
                 User.IsInRole(UserRole.BRANCH_MANAGER.ToString()) ||
                 User.IsInRole(UserRole.STAFF.ToString()));

            var result = await _service.GetByIdAsync(id, branchId, isStaffOrAbove);
            return Ok(ApiResponse<CourtDto>.Ok(result));
        }


        /// <summary>
        /// Thêm sân mới vào chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create(Guid branchId, [FromBody] CreateCourtDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.CreateAsync(
                branchId, dto, currentUserId, currentUserRole);

            return StatusCode(201, ApiResponse<CourtDto>.Ok(result, "Tạo sân thành công"));
        }

        /// <summary>
        /// Cập nhật thông tin sân — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(
            Guid branchId, Guid id, [FromBody] UpdateCourtDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.UpdateAsync(
                id, branchId, dto, currentUserId, currentUserRole);

            return Ok(ApiResponse<CourtDto>.Ok(result));
        }


        /// <summary>
        /// Tạm ngưng sân — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPost("{id:guid}/suspend")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Suspend(Guid branchId, Guid id)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.SuspendAsync(id, branchId, currentUserId, currentUserRole);
            return Ok(ApiResponse<object>.Ok(null!, "Tạm ngưng sân thành công"));
        }

        /// <summary>
        /// Mở lại sân — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPost("{id:guid}/activate")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Activate(Guid branchId, Guid id)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.ActivateAsync(id, branchId, currentUserId, currentUserRole);
            return Ok(ApiResponse<object>.Ok(null!, "Mở lại sân thành công"));
        }

        /// <summary>
        /// Xóa mềm sân — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid branchId, Guid id)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.DeleteAsync(id, branchId, currentUserId, currentUserRole);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa sân thành công"));
        }



    }
}
