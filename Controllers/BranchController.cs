using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Branch;
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

        public BranchController(IBranchService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy danh sách chi nhánh
        /// CUSTOMER / chưa đăng nhập → chỉ thấy ACTIVE
        /// OWNER / MANAGER / STAFF   → thấy cả ACTIVE + SUSPENDED
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
        {
            var includeSuspended = User.Identity?.IsAuthenticated == true &&
            (User.IsInRole(UserRole.OWNER.ToString()) ||
             User.IsInRole(UserRole.BRANCH_MANAGER.ToString()) ||
             User.IsInRole(UserRole.STAFF.ToString()));

            var result = await _service.GetAllAsync(query, includeSuspended);
            return Ok(ApiResponse<PagedResult<BranchDto>>.Ok(result, "Lấy danh sách chi nhánh thành công"));
        }


        /// <summary>
        /// Xem chi tiết chi nhánh
        /// CUSTOMER / chưa đăng nhập → không thấy branch SUSPENDED
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var includeSuspended = User.Identity?.IsAuthenticated == true &&
            (User.IsInRole(UserRole.OWNER.ToString()) ||
             User.IsInRole(UserRole.BRANCH_MANAGER.ToString()) ||
             User.IsInRole(UserRole.STAFF.ToString()));

            var result = await _service.GetByIdAsync(id, includeSuspended);
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
        /// Lấy danh sách dịch vụ tại chi nhánh
        /// </summary>
        [HttpGet("{id:guid}/services")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetServices(Guid id)
        {
            var result = await _service.GetServicesAsync(id);
            return Ok(ApiResponse<List<BranchServiceDto>>.Ok(result, "Lấy danh sách dịch vụ thành công"));
        }

        /// <summary>
        /// Bật dịch vụ vào chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPost("{id:guid}/services")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddService(Guid id, [FromBody] AddServiceToBranchDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.AddServiceAsync(
                id, dto, currentUserId, currentUserRole);

            return StatusCode(201,
                ApiResponse<BranchServiceDto>.Ok(result, "Bật dịch vụ thành công"));
        }


        /// <summary>
        /// Cập nhật giá dịch vụ tại chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpPut("{id:guid}/services/{serviceId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateServicePrice(
            Guid id, Guid serviceId, [FromBody] UpdateBranchServiceDto dto)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.UpdateServicePriceAsync(
                id, serviceId, dto, currentUserId, currentUserRole);

            return Ok(ApiResponse<BranchServiceDto>.Ok(result));
        }

        /// <summary>
        /// Tắt dịch vụ khỏi chi nhánh — OWNER hoặc MANAGER chi nhánh đó
        /// </summary>
        [HttpDelete("{id:guid}/services/{serviceId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DisableService(Guid id, Guid serviceId)
        {
            var currentUserId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            await _service.DisableServiceAsync(
                id, serviceId, currentUserId, currentUserRole);

            return Ok(ApiResponse<object>.Ok(null!, "Tắt dịch vụ thành công"));
        }
    }
}
