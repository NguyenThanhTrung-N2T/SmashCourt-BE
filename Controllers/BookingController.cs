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

        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetAll([FromQuery] BookingListQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetAllAsync(query, userId, role);
            return Ok(ApiResponse<PagedResult<BookingDto>>.Ok(result));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetByIdAsync(id, userId, role);
            return Ok(ApiResponse<BookingDto>.Ok(result));
        }

        [HttpPost("online")]
        [EnableRateLimiting("booking")]
        public async Task<IActionResult> CreateOnline([FromBody] CreateOnlineBookingDto dto)
        {
            Guid? customerId = User.Identity?.IsAuthenticated == true
                ? Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                : null;

            var result = await _service.CreateOnlineAsync(dto, customerId);
            return StatusCode(201, ApiResponse<OnlineBookingResponse>.Ok(result));
        }

        [HttpPost("walk-in")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CreateWalkIn([FromBody] CreateWalkInBookingDto dto)
        {
            var createdBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _service.CreateWalkInAsync(dto, createdBy);
            return StatusCode(201, ApiResponse<BookingDto>.Ok(result, "Tạo đơn thành công"));
        }

        [HttpPost("{id:guid}/cancel")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CancelByStaff(Guid id)
        {
            var cancelledBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.CancelByStaffAsync(id, cancelledBy);
            return Ok(ApiResponse.Ok(message: "Hủy đơn thành công"));
        }

        [HttpPost("{id:guid}/confirm-refund")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> ConfirmRefund(Guid id)
        {
            var confirmedBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _service.ConfirmRefundAsync(id, confirmedBy);
            return Ok(ApiResponse.Ok(message: "Xác nhận hoàn tiền thành công"));
        }

        [HttpGet("cancel/{token}")]
        public async Task<IActionResult> GetCancelInfo(string token)
        {
            var result = await _service.GetCancelInfoAsync(token);
            return Ok(ApiResponse<CancelTokenInfoDto>.Ok(result));
        }

        [HttpPost("cancel/{token}")]
        public async Task<IActionResult> CancelByToken(string token)
        {
            await _service.CancelByTokenAsync(token);
            return Ok(ApiResponse.Ok(message: "Hủy đơn thành công"));
        }

        [HttpPost("{id:guid}/check-in")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> CheckIn(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.CheckInAsync(id, userId, role);
            return Ok(ApiResponse.Ok(message: "Check-in thành công"));
        }

        [HttpPost("{id:guid}/checkout")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> Checkout(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.CheckoutAsync(id, userId, role);
            return Ok(ApiResponse.Ok(message: "Checkout thành công"));
        }

        [HttpPost("{id:guid}/services")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> AddService(
            Guid id, [FromBody] AddBookingServiceDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.AddServiceAsync(id, dto, userId, role);
            return Ok(ApiResponse<BookingDto>.Ok(result));
        }

        [HttpDelete("{id:guid}/services/{bookingServiceId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> RemoveService(Guid id, Guid bookingServiceId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            await _service.RemoveServiceAsync(id, bookingServiceId, userId, role);
            return Ok(ApiResponse.Ok(message: "Xóa dịch vụ thành công"));
        }
    }

    // CustomerBookingController
    [ApiController]
    [Route("api/me/bookings")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    public class CustomerBookingController : ControllerBase
    {
        private readonly IBookingService _service;

        public CustomerBookingController(IBookingService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyBookings([FromQuery] PaginationQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var bookingQuery = new BookingListQuery
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
            var result = await _service.GetAllAsync(bookingQuery, userId, UserRole.CUSTOMER.ToString());
            return Ok(ApiResponse<PagedResult<BookingDto>>.Ok(result));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMyBookingById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _service.GetByIdAsync(id, userId, UserRole.CUSTOMER.ToString());
            return Ok(ApiResponse<BookingDto>.Ok(result));
        }
    }
}
