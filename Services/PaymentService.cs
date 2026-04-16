using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISlotLockRepository _slotLockRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IVnPayService _vnPayService;
        private readonly EmailService _emailService;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            IPaymentRepository paymentRepo,
            IBookingRepository bookingRepo,
            IInvoiceRepository invoiceRepo,
            ISlotLockRepository slotLockRepo,
            ICourtRepository courtRepo,
            IVnPayService vnPayService,
            EmailService emailService,
            ILogger<PaymentService> logger)
        {
            _paymentRepo = paymentRepo;
            _bookingRepo = bookingRepo;
            _invoiceRepo = invoiceRepo;
            _slotLockRepo = slotLockRepo;
            _courtRepo = courtRepo;
            _vnPayService = vnPayService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request)
        {
            // 1. Verify checksum + parse
            var isValid = _vnPayService.VerifyIpn(
                query, out var transactionRef, out var isSuccess, out var rawPayload);

            // 2. Log IPN — luôn luôn log dù hợp lệ hay không
            var payment = await _paymentRepo.GetByTransactionRefAsync(transactionRef);
            await _paymentRepo.CreateIpnLogAsync(new PaymentIpnLog
            {
                PaymentId = payment?.Id,
                Provider = IpnProvider.VNPAY,
                ProviderTransactionId = transactionRef,
                RawPayload = rawPayload,
                IsValid = isValid,
                ProcessedAt = DateTime.UtcNow
            });

            if (!isValid)
            {
                _logger.LogWarning("Invalid VNPay IPN signature: {Ref}", transactionRef);
                return;
            }

            if (payment == null)
            {
                _logger.LogWarning("Payment not found for ref: {Ref}", transactionRef);
                return;
            }

            var booking = payment.Invoice?.Booking;
            if (booking == null) return;

            // 3. Idempotent — đã xử lý rồi thì bỏ qua
            if (booking.Status == BookingStatus.PAID_ONLINE ||
                booking.Status == BookingStatus.CANCELLED)
                return;

            var now = DateTime.UtcNow;

            if (isSuccess)
            {
                // 4A. Thanh toán thành công
                booking.Status = BookingStatus.PAID_ONLINE;
                booking.UpdatedAt = now;
                await _bookingRepo.UpdateAsync(booking);

                payment.Status = PaymentTxStatus.SUCCESS;
                payment.PaidAt = now;
                payment.UpdatedAt = now;
                await _paymentRepo.UpdateAsync(payment);

                var invoice = payment.Invoice!;
                invoice.PaymentStatus = InvoicePaymentStatus.PARTIALLY_PAID;
                invoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(invoice);

                // Xóa slot_lock
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                // Cập nhật court → BOOKED
                foreach (var bc in booking.BookingCourts ?? [])
                {
                    var court = await _courtRepo.GetByIdAsync(bc.CourtId, booking.BranchId);
                    if (court != null)
                    {
                        court.Status = CourtStatus.BOOKED;
                        court.UpdatedAt = now;
                        await _courtRepo.UpdateAsync(court);
                    }
                }

                // Gửi email xác nhận + cancel token
                try
                {
                    await SendConfirmationWithCancelTokenAsync(booking);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirmation email");
                }

                // TODO: Broadcast SignalR
            }
            else
            {
                // 4B. Thanh toán thất bại
                booking.Status = BookingStatus.CANCELLED;
                booking.CancelledAt = now;
                booking.CancelSource = CancelSourceEnum.SYSTEM;
                booking.UpdatedAt = now;

                foreach (var bc in booking.BookingCourts ?? [])
                    bc.IsActive = false;

                await _bookingRepo.UpdateAsync(booking);

                payment.Status = PaymentTxStatus.FAILED;
                payment.UpdatedAt = now;
                await _paymentRepo.UpdateAsync(payment);

                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                foreach (var bc in booking.BookingCourts ?? [])
                {
                    var court = await _courtRepo.GetByIdAsync(bc.CourtId, booking.BranchId);
                    if (court != null)
                    {
                        court.Status = CourtStatus.AVAILABLE;
                        court.UpdatedAt = now;
                        await _courtRepo.UpdateAsync(court);
                    }
                }

                // TODO: Broadcast SignalR
            }
        }

        private async Task SendConfirmationWithCancelTokenAsync(Booking booking)
        {
            var email = booking.Customer?.Email ?? booking.GuestEmail;
            var name = booking.Customer?.FullName ?? booking.GuestName;
            if (string.IsNullOrEmpty(email)) return;

            var rawToken = GenerateCancelToken();
            var tokenHash = HashToken(rawToken);
            
            // Lấy First() an toàn vì đơn luôn có Courts 
            var firstCourtSlot = booking.BookingCourts?.FirstOrDefault();
            if (firstCourtSlot == null) return;
            var startTime = firstCourtSlot.StartTime;

            var tokenExpiry = new DateTime[]
            {
            booking.BookingDate.ToDateTime(startTime),
            DateTimeHelper.GetNowInVietnam().AddHours(24)
            }.Min();

            booking.CancelTokenHash = tokenHash;
            booking.CancelTokenExpiresAt = tokenExpiry;
            await _bookingRepo.UpdateAsync(booking);

            // Fetch lại Court Entity chứa thông tin Sân cho Dto
            var courts = booking.BookingCourts ?? [];
            var courtNamesBuilder = new List<string>();
            foreach (var bc in courts)
            {
                var court = await _courtRepo.GetByIdAsync(bc.CourtId, booking.BranchId);
                if (court != null) courtNamesBuilder.Add(court.Name);
            }
            var courtNames = string.Join(", ", courtNamesBuilder);

            await _emailService.SendBookingConfirmationAsync(
                email, name!, booking.Id, rawToken,
                string.IsNullOrEmpty(courtNames) ? "Hệ thống" : courtNames,
                booking.BookingDate, startTime,
                firstCourtSlot.EndTime);
        }

        private static string GenerateCancelToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
