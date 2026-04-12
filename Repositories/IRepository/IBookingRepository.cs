using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBookingRepository
    {
        Task<PagedResult<Booking>> GetAllAsync(BookingListQuery query, string userRole, Guid userId);
        Task<Booking?> GetByIdAsync(Guid id);
        Task<Booking?> GetByIdWithDetailsAsync(Guid id);
        Task<Booking?> GetByCancelTokenAsync(string tokenHash);
        Task<bool> HasOverlapAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);
        Task<Booking> CreateAsync(Booking booking);
        Task UpdateAsync(Booking booking);

        Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt);
        Task AddPriceItemsAsync(List<BookingPriceItem> items);
        Task AddPromotionAsync(BookingPromotion promotion);
        Task AddServiceAsync(BookingService service);
        Task RemoveServiceAsync(BookingService service);
        Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive);
    }
}
