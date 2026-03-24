using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.DTOs.CancelPolicy;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [Route("api/cancel-policies")]
    [ApiController]
    public class CancelPoliciesController : ControllerBase
    {
        private readonly ICancelPolicyService _cancelPolicyService;

        public CancelPoliciesController(ICancelPolicyService cancelPolicyService)
        {
            _cancelPolicyService = cancelPolicyService;
        }


        /// <summary>
        /// lấy tất cả chính sách hủy
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll()
        {
            var policies = await _cancelPolicyService.GetAllPolicesAsync();
            return Ok(policies);
        }


        /// <summary>
        /// tạo mới chính sách hủy
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "OWNER")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateCancelPolicyDto dto)
        {
            // kiểm tra dữ liệu đầu vào
            if (!ModelState.IsValid)
            {
                return BadRequest("Thông tin không hợp lệ");
            }
            var result = await _cancelPolicyService.CreatePolicyAsync(dto);
            return StatusCode(201, result);
        }

        /// <summary>
        /// cập nhật chính sách hủy
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "OWNER")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCancelPolicyDto dto)
        {
            // kiểm tra dữ liệu đầu vào
            if (!ModelState.IsValid) {
                return BadRequest("Thông tin không hợp lệ");
            }
            var result = await _cancelPolicyService.UpdatePolicyAsync(id, dto);
            return Ok(result);
        }

        /// <summary>
        /// Xóa chính sách hủy
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "OWNER")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _cancelPolicyService.DeletePolicyAsync(id);
            return Ok(new { message = "Xóa chính sách hủy thành công" });
        }
    }
}
