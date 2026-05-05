using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
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


        /// <summary>
        /// Lấy danh sách booking của khách hàng hiện tại
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyBookings([FromQuery] PaginationQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await _service.GetMyBookingsAsync(userId, query);
            return Ok(ApiResponse<PagedResult<BookingDto>>.Ok(result,"Lấy thành công lịch sử thành công của khách hàng"));
        }


        /// <summary>
        /// Lấy thông tin booking của khách hàng hiện tại theo ID
        /// </summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMyBookingById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Service sẽ check booking.CustomerId == userId, throw 403 nếu sai
            var result = await _service.GetByIdAsync(id, userId, UserRole.CUSTOMER.ToString());
            return Ok(ApiResponse<BookingDto>.Ok(result, "Thông tin chi tiết của đặt sân đã tải thành công"));
        }
    }
}
