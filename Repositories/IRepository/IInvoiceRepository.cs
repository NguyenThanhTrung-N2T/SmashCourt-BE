using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IInvoiceRepository
    {
        Task<Invoice?> GetByBookingIdAsync(Guid bookingId);

        // Tạo mới invoice, trả về invoice đã được tạo (có id)
        Task<Invoice> CreateAsync(Invoice invoice);

        // Cập nhật invoice
        Task UpdateAsync(Invoice invoice);
    }
}
