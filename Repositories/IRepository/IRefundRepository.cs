using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IRefundRepository
    {
        // Tạo mới refund, trả về refund đã được tạo (có id)
        Task<Refund> CreateAsync(Refund refund);

        // Cập nhật refund
        Task UpdateAsync(Refund refund);


        // Mọi refund đang chờ của booking (một refund cho mỗi Payment đã thu), kèm Payment
        Task<List<Refund>> GetPendingByBookingIdAsync(Guid bookingId);
    }
}
