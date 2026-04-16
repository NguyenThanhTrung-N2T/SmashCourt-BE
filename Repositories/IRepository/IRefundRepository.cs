using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IRefundRepository
    {
        // Tạo mới refund, trả về refund đã được tạo (có id)
        Task<Refund> CreateAsync(Refund refund);

        // Cập nhật refund
        Task UpdateAsync(Refund refund);

        // Lấy refund theo bookingId, trả về null nếu không tìm thấy
        Task<Refund?> GetByBookingIdAsync(Guid bookingId);
    }
}
