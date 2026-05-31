using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/branches")]
    public class BranchController : ControllerBase
    {
        private readonly IBranchService _service;
        private readonly IBranchUserService _branchUserService;
        private readonly IBranchManagerService _branchManagerService;
        private readonly IBranchStaffService _branchStaffService;

        public BranchController(IBranchService service, IBranchUserService branchUserService, IBranchManagerService branchManagerService, IBranchStaffService branchStaffService)
        {
            _service = service;
            _branchUserService = branchUserService;
            _branchManagerService = branchManagerService;
            _branchStaffService = branchStaffService;
        }

        /// <summary>
        /// Lấy danh sách thông tin cơ bản của chi nhánh đang hoạt động
        /// </summary>
        [HttpGet("basic")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllBasic([FromQuery] PaginationQuery query)
        {
            var result = await _service.GetAllBasicAsync(query);
            return Ok(ApiResponse<PagedResult<BranchBasicDto>>.Ok(result, "Lấy danh sách chi nhánh thành công"));
        }

        /// <summary>
        /// Lấy danh sách chi nhánh đầy đủ — chỉ OWNER
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
        {
            var result = await _service.GetAllAsync(query, includeSuspended: true);
            return Ok(ApiResponse<PagedResult<BranchDto>>.Ok(result, "Lấy danh sách chi nhánh thành công"));
        }


        /// <summary>
        /// Xem thông tin cơ bản của chi nhánh đang hoạt động
        /// </summary>
        [HttpGet("basic/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBasicById(Guid id)
        {
            var result = await _service.GetBasicByIdAsync(id);
            return Ok(ApiResponse<BranchBasicDto>.Ok(result, "Lấy chi tiết chi nhánh thành công"));
        }

        /// <summary>
        /// Xem chi tiết chi nhánh đầy đủ — chỉ OWNER
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id, includeSuspended: true);
            return Ok(ApiResponse<BranchDto>.Ok(result, "Lấy chi tiết chi nhánh thành công"));
        }


        /// <summary>
        /// Tạo chi nhánh mới + gán quản lý — chỉ OWNER
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateBranchDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return StatusCode(201, ApiResponse<BranchDto>.Ok(result, "Tạo chi nhánh thành công"));
        }

        /// <summary>
        /// Cập nhật thông tin chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchDto dto)
        {
            var result = await _service.UpdateAsync(id, dto);
            return Ok(ApiResponse<BranchDto>.Ok(result, "Cập nhật chi nhánh thành công"));
        }


        /// <summary>
        /// Tạm khóa chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPost("{id:guid}/suspend")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Suspend(Guid id)
        {
            await _service.SuspendAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Tạm khóa chi nhánh thành công"));
        }

        /// <summary>
        /// Mở khóa chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPost("{id:guid}/activate")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Activate(Guid id)
        {
            await _service.ActivateAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Mở khóa chi nhánh thành công"));
        }


        /// <summary>
        /// Xoá chi nhánh — chỉ OWNER
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa chi nhánh thành công"));
        }

        /// <summary>
        /// Lấy danh sách loại sân tại chi nhánh
        /// </summary>
        [HttpGet("{id:guid}/court-types")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCourtTypes(Guid id)
        {
            var result = await _service.GetCourtTypesAsync(id);
            return Ok(ApiResponse<List<BranchCourtTypeDto>>.Ok(result, "Lấy danh sách loại sân thành công"));
        }

        /// <summary>
        /// Bật loại sân vào chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPost("{id:guid}/court-types")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddCourtType(
            Guid id, [FromBody] AddCourtTypeToBranchDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.AddCourtTypeAsync(
                id, dto, currentUserId, currentUserRole);

            return StatusCode(201,
                ApiResponse<BranchCourtTypeDto>.Ok(result, "Bật loại sân thành công"));
        }


        /// <summary>
        /// Tắt loại sân khỏi chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpDelete("{id:guid}/court-types/{courtTypeId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveCourtType(Guid id, Guid courtTypeId)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.RemoveCourtTypeAsync(
                id, courtTypeId, currentUserId, currentUserRole);

            return Ok(ApiResponse<object>.Ok(null!, "Tắt loại sân thành công"));
        }

        /// <summary>
        /// Lấy thông tin quản lý chi nhánh hiện tại — chỉ OWNER
        /// </summary>
        [HttpGet("{id:guid}/manager")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetManager(Guid id)
        {
            var result = await _branchManagerService.GetCurrentManagerAsync(id);
            return Ok(ApiResponse<BranchManagerDto?>.Ok(result, "Lấy thông tin quản lý chi nhánh thành công"));
        }

        /// <summary>
        /// Gán quản lý cho chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPost("{id:guid}/manager")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AssignManager(Guid id, [FromBody] AssignManagerDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _branchManagerService.AssignManagerAsync(id, dto, currentUserId);
            return StatusCode(201, ApiResponse<BranchManagerDto>.Ok(result, "Gán quản lý chi nhánh thành công"));
        }

        /// <summary>
        /// Xóa quản lý khỏi chi nhánh — chỉ OWNER
        /// </summary>
        [HttpDelete("{id:guid}/manager")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveManager(Guid id, [FromBody] RemoveManagerDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            await _branchManagerService.RemoveManagerAsync(id, dto, currentUserId);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa quản lý chi nhánh thành công"));
        }

        /// <summary>
        /// Tìm kiếm người dùng để gán vào chi nhánh — chỉ OWNER
        /// </summary>
        [HttpGet("users/search")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SearchUsers([FromQuery] UserSearchQuery query)
        {
            var result = await _branchUserService.SearchUsersAsync(query);
            return Ok(ApiResponse<PagedResult<UserSearchResultDto>>.Ok(result, "Tìm kiếm người dùng thành công"));
        }

        /// <summary>
        /// Lấy danh sách chi nhánh được gán cho người dùng — chỉ OWNER
        /// </summary>
        [HttpGet("users/{userId:guid}/assignments")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserAssignments(Guid userId)
        {
            var result = await _branchUserService.GetUserAssignmentsAsync(userId);
            return Ok(ApiResponse<List<UserBranchAssignmentDto>>.Ok(result, "Lấy danh sách gán chi nhánh thành công"));
        }

        /// <summary>
        /// Lấy danh sách nhân viên chi nhánh với bộ lọc — chỉ OWNER
        /// </summary>
        [HttpGet("{id:guid}/staff")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStaff(Guid id, [FromQuery] StaffFilterQuery query)
        {
            var result = await _branchStaffService.GetStaffAsync(id, query);
            return Ok(ApiResponse<PagedResult<BranchStaffDto>>.Ok(result, "Lấy danh sách nhân viên chi nhánh thành công"));
        }

        /// <summary>
        /// Thêm nhân viên vào chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPost("{id:guid}/staff")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddStaff(Guid id, [FromBody] AddStaffDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _branchStaffService.AddStaffAsync(id, dto, currentUserId);
            return StatusCode(201, ApiResponse<BranchStaffDto>.Ok(result, "Thêm nhân viên chi nhánh thành công"));
        }

        /// <summary>
        /// Xóa nhân viên khỏi chi nhánh — chỉ OWNER
        /// </summary>
        [HttpDelete("{id:guid}/staff/{userId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveStaff(Guid id, Guid userId, [FromBody] RemoveStaffDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            await _branchStaffService.RemoveStaffAsync(id, userId, dto, currentUserId);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa nhân viên chi nhánh thành công"));
        }

        /// <summary>
        /// Thực hiện thao tác hàng loạt với nhân viên chi nhánh — chỉ OWNER
        /// </summary>
        [HttpPost("{id:guid}/staff/bulk")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> BulkStaffOperation(Guid id, [FromBody] BulkStaffOperationDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var result = await _branchStaffService.BulkStaffOperationAsync(id, dto, currentUserId);
            return Ok(ApiResponse<BulkStaffOperationResultDto>.Ok(result, "Thực hiện thao tác hàng loạt thành công"));
        }
    }
}
