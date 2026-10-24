using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
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
                // IPN tăng lượt khuyến mãi theo BookingPromotion; thiếu Include thì đơn online không bao giờ được tính lượt
                .Include(p => p.Invoice)
                    .ThenInclude(i => i.Booking)
                        .ThenInclude(b => b.BookingPromotion)
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
        public async Task<decimal> GetCollectedAmountAsync(Guid invoiceId)
        {
            return await _context.Payments
                .Where(p =>
                    p.InvoiceId == invoiceId &&
                    p.Status == PaymentTxStatus.SUCCESS)
                .SumAsync(p => p.Amount - p.RefundedAmount);
        }
        public async Task<bool> TryMarkSuccessAsync(Guid paymentId, DateTime nowUtc)
        {
            return await _context.Payments
                .Where(p => p.Id == paymentId && p.Status != PaymentTxStatus.SUCCESS)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentTxStatus.SUCCESS)
                    .SetProperty(p => p.PaidAt, (DateTime?)nowUtc)
                    .SetProperty(p => p.UpdatedAt, nowUtc)) == 1;
        }

        public async Task<bool> TryFailPendingAsync(Guid paymentId, DateTime nowUtc)
        {
            return await _context.Payments
                .Where(p => p.Id == paymentId && p.Status == PaymentTxStatus.PENDING)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentTxStatus.FAILED)
                    .SetProperty(p => p.UpdatedAt, nowUtc)) == 1;
        }

        public async Task<int> FailPendingByInvoiceAsync(Guid invoiceId, Guid? exceptPaymentId, DateTime nowUtc)
        {
            return await _context.Payments
                .Where(p => p.InvoiceId == invoiceId && p.Status == PaymentTxStatus.PENDING && p.Id != exceptPaymentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentTxStatus.FAILED)
                    .SetProperty(p => p.UpdatedAt, nowUtc));
        }

        public async Task<List<Payment>> GetSuccessfulByInvoiceAsync(Guid invoiceId)
        {
            return await _context.Payments
                .AsNoTracking()
                .Where(p => p.InvoiceId == invoiceId && p.Status == PaymentTxStatus.SUCCESS)
                .ToListAsync();
        }

        public async Task CreateIpnLogAsync(PaymentIpnLog log)
        {
            _context.PaymentIpnLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
