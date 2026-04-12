using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IInvoiceRepository
    {
        Task<Invoice?> GetByBookingIdAsync(Guid bookingId);
        Task<Invoice> CreateAsync(Invoice invoice);
        Task UpdateAsync(Invoice invoice);
    }
}
