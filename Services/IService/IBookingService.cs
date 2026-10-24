using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingService
    {
        Task<BookingDto> GetByIdAsync(Guid bookingId, Guid userId, string userRole);
        // lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<BookingDto>> GetAllAsync(BookingListQuery query, Guid currentUserId, string currentUserRole);

        Task<List<BookingScheduleCourtDto>> GetScheduleAsync(BookingScheduleQuery query, Guid currentUserId, string currentUserRole);

        Task<BookingDashboardSummaryDto> GetDashboardSummaryAsync(BookingDashboardSummaryQuery query, Guid currentUserId, string currentUserRole);

        Task<List<BookingCalendarHeatmapDto>> GetCalendarHeatmapAsync(BookingCalendarHeatmapQuery query, Guid currentUserId, string currentUserRole);

        // Lấy danh sách booking của khách hàng (có phân trang và filter)
        Task<PagedResult<BookingDto>> GetMyBookingsAsync(Guid customerId, BookingListQuery query);

        // Lấy thông tin booking theo id, có phân quyền
        Task<BookingDto> GetDetailsByIdAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Tạo booking online cho khách đã đăng nhập: giữ chỗ, hỏi quyết định cọc, rồi áp nhánh xử lý
        Task<OnlineBookingResponse> CreateOnlineAsync(CreateOnlineBookingDto dto, Guid customerId);

        // Đặt lịch trực tiếp tại quầy (walk-in booking) bởi nhân viên, có thể có hoặc không có customerId, nếu không có thì sẽ tạo booking với thông tin khách vãng lai
        Task<BookingDto> CreateWalkInAsync(CreateWalkInBookingDto dto, Guid createdBy);


        // Check-in booking bởi nhân viên (staff)
        Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Check-out booking bởi nhân viên (staff)
        Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Thu tiền cho booking đang chờ (PENDING_PAYMENT) và hoàn tất đơn (staff)
        Task CollectPaymentAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Thêm dịch vụ vào booking bởi nhân viên (staff)
        Task<BookingDto> AddServiceAsync(Guid id, AddBookingServiceDto dto, Guid currentUserId, string currentUserRole);

        // Xóa dịch vụ khỏi booking bởi nhân viên (staff)
        Task<BookingDto> RemoveServiceAsync(Guid id, Guid serviceId, Guid currentUserId, string currentUserRole);

        // Xác nhận hoàn tiền cho booking đã hủy bởi nhân viên (staff)
        Task ConfirmRefundAsync(Guid id, Guid confirmedBy, string currentUserRole);

    }
}
