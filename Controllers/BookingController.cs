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
        /// Lấy lịch booking theo sân trong một ngày
        /// </summary>
        /// <param name="query">Query parameters: BranchId (optional), Date (required)</param>
        /// <returns>Danh sách sân kèm lịch booking trong ngày</returns>
        /// <remarks>
        /// Endpoint này dùng để hiển thị lịch đặt sân của tất cả các sân trong 1 ngày cụ thể.
        /// 
        /// **Use case:**
        /// - Staff/Manager mở app vào buổi sáng → xem lịch hôm nay có bao nhiêu booking
        /// - Biết sân nào đang trống, sân nào đang có người chơi
        /// - Click vào booking → xem chi tiết khách hàng
        /// 
        /// **Phân quyền:**
        /// - OWNER: Xem tất cả branches (nếu không truyền BranchId)
        /// - MANAGER/STAFF: Chỉ xem branch của mình (BranchId bị override)
        /// 
        /// **Response:**
        /// - Trả về danh sách sân, mỗi sân có danh sách booking trong ngày
        /// - Chỉ hiển thị booking có status ACTIVE (PENDING, CONFIRMED, PAID_ONLINE, IN_PROGRESS, PENDING_PAYMENT)
        /// - Sắp xếp theo tên sân và thời gian bắt đầu
        /// </remarks>
        /// <response code="200">Lấy lịch đặt sân thành công</response>
        /// <response code="400">BranchId không hợp lệ hoặc thiếu Date</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền truy cập</response>
        [HttpGet("schedule")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetSchedule([FromQuery] BookingScheduleQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetScheduleAsync(query, userId, role);
            return Ok(ApiResponse<List<BookingScheduleCourtDto>>.Ok(result, "Lấy lịch đặt sân thành công"));
        }


        /// <summary>
        /// Lấy thống kê nhanh cho dashboard booking
        /// </summary>
        /// <param name="query">Query parameters: BranchId (optional)</param>
        /// <returns>Thống kê tổng quan booking hôm nay</returns>
        /// <remarks>
        /// Endpoint này dùng để hiển thị dashboard tổng quan cho Staff/Manager.
        /// 
        /// **Use case:**
        /// - Manager mở app → nhìn 1 cái biết ngay tình hình kinh doanh hôm nay
        /// - Hiển thị trên màn hình "Dashboard Booking" hoặc "Trang chủ Staff"
        /// 
        /// **Dữ liệu trả về:**
        /// - TodayBookings: Tổng số booking hôm nay
        /// - ActiveBookings: Số booking đang chơi (IN_PROGRESS)
        /// - CompletedBookings: Số booking đã hoàn thành
        /// - CancelledBookings: Số booking đã hủy
        /// - TodayRevenue: Doanh thu hôm nay (chỉ tính booking đã thanh toán)
        /// - PendingRefunds: Số đơn chờ hoàn tiền (cần xử lý)
        /// 
        /// **Phân quyền:**
        /// - OWNER: Xem tất cả branches (nếu không truyền BranchId)
        /// - MANAGER/STAFF: Chỉ xem branch của mình (BranchId bị override)
        /// 
        /// **Lưu ý:**
        /// - Chỉ tính revenue từ booking có PaymentStatus = PAID
        /// - Không tính booking đã hủy vào revenue
        /// - PendingRefunds là tổng số đơn chờ hoàn tiền (không chỉ hôm nay)
        /// </remarks>
        /// <response code="200">Lấy thống kê booking thành công</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền truy cập</response>
        [HttpGet("dashboard-summary")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetDashboardSummary([FromQuery] BookingDashboardSummaryQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetDashboardSummaryAsync(query, userId, role);
            return Ok(ApiResponse<BookingDashboardSummaryDto>.Ok(result, "Lấy thống kê booking thành công"));
        }


        /// <summary>
        /// Lấy dữ liệu heatmap booking theo tháng
        /// </summary>
        /// <param name="query">Query parameters: Year (required), Month (required), BranchId (optional)</param>
        /// <returns>Danh sách dữ liệu booking theo từng ngày trong tháng</returns>
        /// <remarks>
        /// Endpoint này dùng để hiển thị calendar heatmap (giống GitHub contribution graph).
        /// 
        /// **Use case:**
        /// - Manager xem report tháng → biết ngày nào đông khách, ngày nào vắng
        /// - Phân tích xu hướng booking trong tháng
        /// - Hiển thị trên màn hình "Phân tích Booking" hoặc "Report"
        /// 
        /// **Dữ liệu trả về (cho mỗi ngày):**
        /// - Date: Ngày (format: yyyy-MM-dd)
        /// - BookingCount: Số lượng booking trong ngày
        /// - OccupancyRate: Tỷ lệ sử dụng sân (0.0 - 1.0)
        /// - Revenue: Doanh thu trong ngày
        /// 
        /// **Cách tính OccupancyRate:**
        /// - dailyAvailableHours = (closeTime - openTime) × số sân
        /// - bookedHours = tổng giờ đã đặt trong ngày
        /// - occupancyRate = bookedHours / dailyAvailableHours
        /// 
        /// **Phân quyền:**
        /// - OWNER: Xem tất cả branches (nếu không truyền BranchId)
        /// - MANAGER/STAFF: Chỉ xem branch của mình (BranchId bị override)
        /// 
        /// **Lưu ý:**
        /// - Chỉ tính booking có status hợp lệ (PENDING, CONFIRMED, PAID_ONLINE, IN_PROGRESS, PENDING_PAYMENT, COMPLETED)
        /// - Không tính booking đã hủy hoặc NO_SHOW
        /// - Nếu không truyền Month, mặc định lấy tháng hiện tại
        /// </remarks>
        /// <response code="200">Lấy dữ liệu heatmap thành công</response>
        /// <response code="400">Year hoặc Month không hợp lệ</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền truy cập</response>
        [HttpGet("calendar-heatmap")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        public async Task<IActionResult> GetCalendarHeatmap([FromQuery] BookingCalendarHeatmapQuery query)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await _service.GetCalendarHeatmapAsync(query, userId, role);
            return Ok(ApiResponse<List<BookingCalendarHeatmapDto>>.Ok(result, "Lấy dữ liệu heatmap thành công"));
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
