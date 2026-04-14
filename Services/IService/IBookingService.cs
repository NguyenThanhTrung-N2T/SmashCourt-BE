using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingService
    {
        // lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<BookingDto>> GetAllAsync(BookingListQuery query, Guid currentUserId, string currentUserRole);
        
        
        Task<PagedResult<BookingDto>> GetMyBookingsAsync(Guid customerId, PaginationQuery query);

        // Lấy thông tin booking theo id, có phân quyền
        Task<BookingDto> GetByIdAsync(Guid id, Guid currentUserId, string currentUserRole);

        // Tạo booking online (có thể có hoặc không có customerId, nếu không có thì sẽ tạo booking với thông tin khách vãng lai)
        Task<OnlineBookingResponse> CreateOnlineAsync(CreateOnlineBookingDto dto, Guid? customerId);

        Task<BookingDto> CreateWalkInAsync(CreateWalkInBookingDto dto, Guid createdBy);
        Task CancelByStaffAsync(Guid id, Guid cancelledBy, string currentUserRole);
        Task<CancelTokenInfoDto> GetCancelInfoAsync(string token);
        Task CancelByTokenAsync(string token);
        Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole);
        Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole);
        Task<BookingDto> AddServiceAsync(Guid id, AddBookingServiceDto dto, Guid currentUserId, string currentUserRole);
        Task<BookingDto> RemoveServiceAsync(Guid id, Guid serviceId, Guid currentUserId, string currentUserRole);
        Task ConfirmRefundAsync(Guid id, Guid confirmedBy, string currentUserRole);
    }
}
