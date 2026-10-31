using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Repositories
{
    public class RefundRepository : IRefundRepository
    {
        private readonly SmashCourtContext _context;

        public RefundRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<Refund> CreateAsync(Refund refund)
        {
            _context.Refunds.Add(refund);
            await _context.SaveChangesAsync();
            return refund;
        }

        public async Task UpdateAsync(Refund refund)
        {
            _context.Refunds.Update(refund);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Refund>> GetPendingByBookingIdAsync(Guid bookingId)
        {
            return await _context.Refunds
                .Include(r => r.Payment)
                .Where(r => r.Payment.Invoice.BookingId == bookingId && r.Status == RefundStatus.PENDING)
                .ToListAsync();
        }

    }
}
