using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBookingRepository
    {
        // Lấy danh sách booking để hiển thị cho staff (có filter, phân trang)
        Task<PagedResult<Booking>> GetAllAsync(BookingListQuery query, string userRole, Guid userId);

        // Lấy danh sách booking của khách hàng (có phân trang)
        Task<PagedResult<Booking>> GetByCustomerIdAsync(Guid customerId, PaginationQuery query);

        // Lấy thông tin booking theo id, có phân quyền
        Task<Booking?> GetByIdWithDetailsAsync(Guid id);

        // Lấy thông tin booking theo token hủy (dùng cho khách hàng hủy booking online)
        Task<Booking?> GetByCancelTokenAsync(string tokenHash);

        // Kiểm tra xem có booking nào đã tồn tại trên sân vào khung giờ đó hay không (dùng để validate khi tạo hoặc cập nhật booking)
        Task<bool> HasOverlapAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        // Lấy danh sách booking court đang active (đang check-in) của một sân trong một ngày cụ thể
        Task<List<BookingCourt>> GetActiveByCourtAndDateAsync(Guid courtId, DateOnly date);

        // Tạo mới booking, trả về booking đã được tạo (có id)
        Task<Booking> CreateAsync(Booking booking);

        // Cập nhật booking
        Task UpdateAsync(Booking booking);

        // Thêm mới booking court, trả về booking court đã được tạo (có id)
        Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt);

        // Thêm price item cho slot đặt sân trong booking court
        Task AddPriceItemsAsync(List<BookingPriceItem> items);

        // Thêm promotion vào booking
        Task AddPromotionAsync(BookingPromotion promotion);

        // Thêm dịch vụ vào booking
        Task AddServiceAsync(BookingService service);

        // Xóa dịch vụ khỏi booking
        Task RemoveServiceAsync(BookingService service);

        // Cập nhật trạng thái active của booking court (dùng để check-in/check-out)
        Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive);

        // Atomic consume token để tránh race condition khi hủy booking qua link
        Task<bool> TryConsumeTokenAsync(Guid bookingId, string tokenHash, DateTime consumedAt);
    }
}
