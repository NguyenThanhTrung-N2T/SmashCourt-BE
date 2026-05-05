using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/bookings")]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _service;

        public BookingController(IBookingService service)
        {
            _service = service;
        }

        /// <summary>
        /// Lấy danh sách booking
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetAll([FromQuery] BookingListQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetAllAsync(query, userId, role);
            return Ok(ApiResponse<PagedResult<BookingDto>>.Ok(result, "Lấy danh sách đặt sân thành công"));
        }


        /// <summary>
        /// Lấy thông tin booking theo id
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetByIdAsync(id, userId, role);
            return Ok(ApiResponse<BookingDto>.Ok(result, "Lấy thông tin đặt sân thành công"));
        }


        /// <summary>
        /// Đặt sân online - khách hàng có thể đặt sân mà không cần đăng nhập, nhưng nếu đã đăng nhập thì sẽ gắn booking với tài khoản đó
        /// </summary>
        [HttpPost("online")]
        [EnableRateLimiting("booking")]
        public async Task<IActionResult> CreateOnline([FromBody] CreateOnlineBookingDto dto)
        {
            Guid? customerId = User.Identity?.IsAuthenticated == true
                ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                : null;

            var result = await _service.CreateOnlineAsync(dto, customerId);
            return StatusCode(201, ApiResponse<OnlineBookingResponse>.Ok(result,"Đặt sân online thành công"));
        }


        /// <summary>
        /// Đặt sân tại quầy - chỉ nhân viên mới được tạo booking theo cách này, thường dùng cho khách walk-in hoặc khách gọi điện đặt sân
        /// </summary>
        [HttpPost("walk-in")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CreateWalkIn([FromBody] CreateWalkInBookingDto dto)
        {
            var createdBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _service.CreateWalkInAsync(dto, createdBy);
            return StatusCode(201, ApiResponse<BookingDto>.Ok(result, "Tạo đơn thành công"));
        }


        /// <summary>
        /// Hủy đơn đặt sân
        /// </summary>
        [HttpPost("{id:guid}/cancel")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CancelByStaff(Guid id)
        {
            var cancelledBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.CancelByStaffAsync(id, cancelledBy, role);
            return Ok(ApiResponse.Ok(message: "Hủy đơn thành công"));
        }


        /// <summary>
        /// Xác nhận hoàn tiền
        /// </summary>
        [HttpPost("{id:guid}/confirm-refund")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> ConfirmRefund(Guid id)
        {
            var confirmedBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.ConfirmRefundAsync(id, confirmedBy, role);
            return Ok(ApiResponse.Ok(message: "Xác nhận hoàn tiền thành công"));
        }


        /// <summary>
        /// Xem thông tin hủy đơn qua link (token) 
        /// </summary>
        [HttpGet("cancel/{token}")]
        public async Task<IActionResult> GetCancelInfo(string token)
        {
            var result = await _service.GetCancelInfoAsync(token);
            return Ok(ApiResponse<CancelTokenInfoDto>.Ok(result,"Lấy thông tin đặt sân thành công"));
        }


        /// <summary>
        /// Hủy đơn đặt sân qua link (token)
        /// </summary>
        [HttpPost("cancel/{token}")]
        public async Task<IActionResult> CancelByToken(string token)
        {
            await _service.CancelByTokenAsync(token);
            return Ok(ApiResponse.Ok(message: "Hủy đơn thành công"));
        }


        /// <summary>
        /// Check - in khách hàng đến sân, chỉ nhân viên mới được check-in
        /// </summary>
        [HttpPost("{id:guid}/check-in")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CheckIn(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.CheckInAsync(id, userId, role);
            return Ok(ApiResponse.Ok(message: "Check-in thành công"));
        }


        /// <summary>
        /// Check out khách hàng rời sân, chỉ nhân viên mới được check-out
        /// </summary>
        [HttpPost("{id:guid}/checkout")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> Checkout(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.CheckoutAsync(id, userId, role);
            return Ok(ApiResponse.Ok(message: "Checkout thành công"));
        }


        /// <summary>
        /// Thêm dịch vụ cho booking, chỉ nhân viên mới được thêm dịch vụ
        /// </summary>
        [HttpPost("{id:guid}/services")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> AddService(
            Guid id, [FromBody] AddBookingServiceDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.AddServiceAsync(id, dto, userId, role);
            return StatusCode(201, ApiResponse<BookingDto>.Ok(result, "Thêm dịch vụ thành công"));
        }


        /// <summary>
        /// Xoá dịch vụ khỏi booking, chỉ nhân viên mới được xoá dịch vụ
        /// </summary>
        [HttpDelete("{id:guid}/services/{serviceId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> RemoveService(Guid id, Guid serviceId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.RemoveServiceAsync(id, serviceId, userId, role);
            return Ok(ApiResponse<BookingDto>.Ok(result, "Xóa dịch vụ thành công"));
        }
    }
}
