using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingService
    {
        Task<PagedResult<BookingDto>> GetAllAsync(BookingListQuery query, Guid currentUserId, string currentUserRole);
        Task<BookingDto> GetByIdAsync(Guid id, Guid currentUserId, string currentUserRole);
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
