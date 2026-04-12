using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IRefundRepository
    {
        Task<Refund> CreateAsync(Refund refund);
        Task UpdateAsync(Refund refund);
        Task<Refund?> GetByBookingIdAsync(Guid bookingId);
    }
}
