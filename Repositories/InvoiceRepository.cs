using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly SmashCourtContext _context;

        public InvoiceRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<Invoice?> GetByBookingIdAsync(Guid bookingId)
        {
            return await _context.Invoices
                .FirstOrDefaultAsync(i => i.BookingId == bookingId);
        }

        // Tạo mới invoice, trả về invoice đã được tạo (có id)
        public async Task<Invoice> CreateAsync(Invoice invoice)
        {
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();
            return invoice;
        }

        public async Task UpdateAsync(Invoice invoice)
        {
            _context.Invoices.Update(invoice);
            await _context.SaveChangesAsync();
        }
    }
}
