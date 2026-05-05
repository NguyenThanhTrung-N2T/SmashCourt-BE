using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly SmashCourtContext _context;

        public PaymentRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<Payment?> GetByTransactionRefAsync(string transactionRef)
        {
            return await _context.Payments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.BookingCourts)
                            .ThenInclude(bc => bc.Court)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.Customer)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.Branch)
                .FirstOrDefaultAsync(p => p.TransactionRef == transactionRef);
        }

        public async Task<Payment?> GetByTransactionRefAsync(string transactionRef, bool asNoTracking)
        {
            var query = _context.Payments
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.BookingCourts)
                            .ThenInclude(bc => bc.Court)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.Customer)
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.Branch)
                .Where(p => p.TransactionRef == transactionRef);

            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync();
        }

        public async Task<Payment> CreateAsync(Payment payment)
        {
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task UpdateAsync(Payment payment)
        {
            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();
        }

        public async Task CreateIpnLogAsync(PaymentIpnLog log)
        {
            _context.PaymentIpnLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
