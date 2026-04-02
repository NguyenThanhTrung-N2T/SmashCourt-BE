using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Promotion;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/promotions")]
    public class PromotionController : ControllerBase
    {
        private readonly IPromotionService _service;

        public PromotionController(IPromotionService service)
        {
            _service = service;
        }

        /// <summary>
        /// Danh sách tất cả promotion — OWNER/MANAGER/STAFF
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] PaginationQuery query)
        {
            var result = await _service.GetAllAsync(query);
            return Ok(ApiResponse<PagedResult<PromotionDto>>.Ok(result,"Lấy danh sách khuyến mãi thành công"));
        }

        /// <summary>
        /// Danh sách promotion đang ACTIVE — dùng khi đặt sân
        /// </summary>
        [HttpGet("active")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetActive()
        {
            var result = await _service.GetActiveAsync();
            return Ok(ApiResponse<List<PromotionDto>>.Ok(result, "Lấy danh sách khuyến mãi đang hoạt động thành công"));
        }

        /// <summary>
        /// Chi tiết 1 promotion — OWNER/MANAGER/STAFF
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _service.GetByIdAsync(id);
            return Ok(ApiResponse<PromotionDto>.Ok(result, "Lấy chi tiết khuyến mãi thành công"));
        }


        /// <summary>
        /// Tạo promotion mới — chỉ OWNER
        /// </summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreatePromotionDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return StatusCode(201,
                ApiResponse<PromotionDto>.Ok(result, "Tạo khuyến mãi thành công"));
        }

        /// <summary>
        /// Cập nhật promotion — chỉ OWNER
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePromotionDto dto)
        {
            var result = await _service.UpdateAsync(id, dto);
            return Ok(ApiResponse<PromotionDto>.Ok(result, "Cập nhật khuyến mãi thành công"));
        }

        /// <summary>
        /// Xóa mềm promotion — chỉ OWNER
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _service.DeleteAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Xóa khuyến mãi thành công"));
        }

    }
}
