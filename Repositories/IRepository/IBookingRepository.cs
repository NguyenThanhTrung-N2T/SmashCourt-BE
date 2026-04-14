using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBookingRepository
    {
        // Lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<Booking>> GetAllAsync(BookingListQuery query, string userRole, Guid userId);
        
        Task<PagedResult<Booking>> GetByCustomerIdAsync(Guid customerId, PaginationQuery query);
        Task<Booking?> GetByIdAsync(Guid id);

        // Lấy thông tin booking theo id, có phân quyền
        Task<Booking?> GetByIdWithDetailsAsync(Guid id);

        Task<Booking?> GetByCancelTokenAsync(string tokenHash);

        // Kiểm tra xem có booking nào đã tồn tại trên sân vào khung giờ đó hay không (dùng để validate khi tạo hoặc cập nhật booking)
        Task<bool> HasOverlapAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);



        Task<List<BookingCourt>> GetActiveByCourtAndDateAsync(Guid courtId, DateOnly date);

        // Tạo mới booking, trả về booking đã được tạo (có id)
        Task<Booking> CreateAsync(Booking booking);

        
        Task UpdateAsync(Booking booking);

        // Thêm mới booking court, trả về booking court đã được tạo (có id)
        Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt);

        // Thêm price item cho slot đặt sân trong booking court
        Task AddPriceItemsAsync(List<BookingPriceItem> items);

        // Thêm promotion vào booking
        Task AddPromotionAsync(BookingPromotion promotion);
        Task AddServiceAsync(BookingService service);
        Task RemoveServiceAsync(BookingService service);
        Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive);
    }
}
