using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;

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
    }
}
