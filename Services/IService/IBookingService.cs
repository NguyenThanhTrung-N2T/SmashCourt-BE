using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingService
    {
        // lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<BookingDto>> GetAllAsync(BookingListQuery query, Guid currentUserId, string currentUserRole);

        // Lấy danh sách booking của khách hàng (có phân trang)
        Task<PagedResult<BookingDto>> GetMyBookingsAsync(Guid customerId, PaginationQuery query);

        // Lấy thông tin booking theo id, có phân quyền
        Task<BookingDto> GetByIdAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Tạo booking online (có thể có hoặc không có customerId, nếu không có thì sẽ tạo booking với thông tin khách vãng lai)
        Task<OnlineBookingResponse> CreateOnlineAsync(CreateOnlineBookingDto dto, Guid? customerId);

        // Đặt lịch trực tiếp tại quầy (walk-in booking) bởi nhân viên, có thể có hoặc không có customerId, nếu không có thì sẽ tạo booking với thông tin khách vãng lai
        Task<BookingDto> CreateWalkInAsync(CreateWalkInBookingDto dto, Guid createdBy);

        // Hủy booking bởi nhân viên (staff) 
        Task CancelByStaffAsync(Guid id, Guid cancelledBy, string currentUserRole);

        // Lấy thông tin hủy booking theo token (dùng cho khách hàng hủy booking online)
        Task<CancelTokenInfoDto> GetCancelInfoAsync(string token);

        // Hủy booking bởi khách hàng thông qua token (dùng cho khách hàng hủy booking online)
        Task CancelByTokenAsync(string token);

        // Check-in booking bởi nhân viên (staff)
        Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Check-out booking bởi nhân viên (staff)
        Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Thêm dịch vụ vào booking bởi nhân viên (staff)
        Task<BookingDto> AddServiceAsync(Guid id, AddBookingServiceDto dto, Guid currentUserId, string currentUserRole);

        // Xóa dịch vụ khỏi booking bởi nhân viên (staff)
        Task<BookingDto> RemoveServiceAsync(Guid id, Guid serviceId, Guid currentUserId, string currentUserRole);

        // Xác nhận hoàn tiền cho booking đã hủy bởi nhân viên (staff)
        Task ConfirmRefundAsync(Guid id, Guid confirmedBy, string currentUserRole);
    }
}
