using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IInvoiceRepository
    {
        Task<Invoice?> GetByBookingIdAsync(Guid bookingId);

        // Tạo mới invoice, trả về invoice đã được tạo (có id)
        Task<Invoice> CreateAsync(Invoice invoice);

        // Cập nhật invoice
        Task UpdateAsync(Invoice invoice);

        // Đổi trạng thái thanh toán bằng một câu lệnh (không ghi đè các cột khác của hóa đơn)
        Task SetPaymentStatusAsync(Guid invoiceId, InvoicePaymentStatus status, DateTime nowUtc);
    }
}
