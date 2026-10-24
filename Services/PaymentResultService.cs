using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Nghiệp vụ khi cổng thanh toán báo kết quả, dùng chung cho mọi cổng. Chống lặp theo trạng thái Payment; mọi lệnh ghi có điều kiện
/// nên kết quả đến trùng lúc với lệnh hủy hay kết quả khác vẫn không mất tiền.
/// </summary>
public class PaymentResultService : IPaymentResultService
{
    private enum SuccessOutcome { AlreadyRecorded, Confirmed, LatePaymentRefund, ExtraPayment }

    private readonly IPaymentRepository _paymentRepo;
    private readonly IBookingRepository _bookingRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly ISlotLockRepository _slotLockRepo;
    private readonly IRefundRepository _refundRepo;
    private readonly PromotionEngineService _promotionEngine;
    private readonly IBookingNotificationService _notifications;
    private readonly IOptionsMonitor<BookingPaymentSettings> _paymentSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentResultService> _logger;

    public PaymentResultService(
        IPaymentRepository paymentRepo,
        IBookingRepository bookingRepo,
        IInvoiceRepository invoiceRepo,
        ISlotLockRepository slotLockRepo,
        IRefundRepository refundRepo,
        PromotionEngineService promotionEngine,
        IBookingNotificationService notifications,
        IOptionsMonitor<BookingPaymentSettings> paymentSettings,
        TimeProvider timeProvider,
        ILogger<PaymentResultService> logger)
    {
        _paymentRepo = paymentRepo;
        _bookingRepo = bookingRepo;
        _invoiceRepo = invoiceRepo;
        _slotLockRepo = slotLockRepo;
        _refundRepo = refundRepo;
        _promotionEngine = promotionEngine;
        _notifications = notifications;
        _paymentSettings = paymentSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ApplyAsync(GatewayPaymentResult result)
    {
        var now = UtcNow();
        var payment = await _paymentRepo.GetByTransactionRefAsync(result.TransactionRef);

        // Lưu mọi kết quả, kể cả chữ ký sai, để đối soát với cổng
        await _paymentRepo.CreateIpnLogAsync(new PaymentIpnLog
        {
            PaymentId = payment?.Id,
            Provider = result.Provider,
            ProviderTransactionId = result.TransactionRef,
            RawPayload = result.RawPayload,
            IsValid = result.IsSignatureValid,
            ProcessedAt = now
        });

        if (!result.IsSignatureValid)
        {
            _logger.LogWarning("Kết quả {Provider} sai chữ ký, bỏ qua: {Ref}", result.Provider, result.TransactionRef);
            return;
        }
        if (payment?.Invoice?.Booking is not { } booking)
        {
            _logger.LogWarning("Không tìm thấy Payment hoặc booking của giao dịch {Ref}", result.TransactionRef);
            return;
        }
        // Chặn kết quả bị sửa số tiền
        if (result.Amount != payment.Amount)
        {
            _logger.LogWarning("Số tiền của giao dịch {Ref} lệch: Payment {Expected}, cổng gửi {Actual}; bỏ qua",
                result.TransactionRef, payment.Amount, result.Amount);
            return;
        }

        if (result.IsSuccess)
            await ApplySuccessAsync(payment, booking, now);
        else
            await ApplyFailureAsync(payment, booking, result.ResponseCode, now);
    }

    // Thất bại: chỉ Payment còn chờ mới bị đánh FAILED; đơn không bị hủy mà được giữ thêm tối đa ân hạn để khách thanh toán lại
    private async Task ApplyFailureAsync(Payment payment, Booking booking, string responseCode, DateTime now)
    {
        DateTime? holdUntil = null;
        using (var transaction = TransactionScopeHelper.ReadCommitted())
        {
            await _bookingRepo.LockAsync(booking.Id);
            if (!await _paymentRepo.TryFailPendingAsync(payment.Id, now))
            {
                _logger.LogInformation("Kết quả thất bại của Payment {PaymentId} không còn chờ (link cũ hoặc gửi lặp), bỏ qua", payment.Id);
                return;
            }

            if (payment.Invoice.ExpiresAt is { } expiresAt)
            {
                var shortened = PaymentHoldPolicy.ForPaymentFailure(
                    now, expiresAt, _paymentSettings.CurrentValue.PaymentFailureGraceMinutes);
                if (await _bookingRepo.ShortenPaymentWindowAsync(booking.Id, shortened, now))
                    holdUntil = shortened;
            }
            transaction.Complete();
        }

        payment.Status = PaymentTxStatus.FAILED;
        _logger.LogInformation("Thanh toán thất bại (mã {Code}) cho booking {BookingId}; giữ đơn đến {HoldUntil}",
            responseCode, booking.Id, holdUntil);
        await _notifications.NotifyPaymentFailedAsync(booking, payment, holdUntil);
    }

    private async Task ApplySuccessAsync(Payment payment, Booking booking, DateTime now)
    {
        var outcome = SuccessOutcome.AlreadyRecorded;
        BookingStatus? currentStatus = null;
        string? rawCancelToken = null;
        var invoice = payment.Invoice;

        using (var transaction = TransactionScopeHelper.ReadCommitted())
        {
            // Khóa booking trước Payment, cùng thứ tự với hủy, thanh toán lại, check-out
            await _bookingRepo.LockAsync(booking.Id);
            if (!await _paymentRepo.TryMarkSuccessAsync(payment.Id, now))
            {
                _logger.LogInformation("Payment {PaymentId} đã được ghi nhận trước đó, bỏ qua", payment.Id);
                return;
            }

            var token = CancelTokenHelper.Generate(PlayStartUtc(booking), now);
            if (await _bookingRepo.AtomicUpdatePaymentSuccessAsync(
                    booking.Id, BookingStatus.PENDING, token.TokenHash, token.ExpiresAtUtc, now) == 1)
            {
                await _invoiceRepo.SetPaymentStatusAsync(invoice.Id, InvoicePaymentStatus.PARTIALLY_PAID, now);
                await _paymentRepo.FailPendingByInvoiceAsync(invoice.Id, payment.Id, now);
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);
                if (booking.BookingPromotion != null)
                    await _promotionEngine.IncrementUsageCountAsync(booking.BookingPromotion.PromotionId);
                rawCancelToken = token.RawToken;
                outcome = SuccessOutcome.Confirmed;
            }
            else
            {
                // Đơn không còn chờ thanh toán: đọc lại để biết tiền về đơn đã hủy hay đơn đã được trả bằng link khác
                currentStatus = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                if (BookingStatusTransition.GetCancelledStatuses().Contains(currentStatus.Value))
                {
                    await _refundRepo.CreateAsync(new Refund
                    {
                        PaymentId = payment.Id,
                        Amount = payment.Amount,
                        RefundPercent = 100,
                        Status = RefundStatus.PENDING,
                        CreatedAt = now
                    });
                    // Ném để rollback và cổng gửi lại kết quả khi trạng thái đổi giữa lúc đọc và lúc ghi
                    if (currentStatus != BookingStatus.CANCELLED_PENDING_REFUND
                        && !await _bookingRepo.MarkPendingRefundAsync(booking.Id, currentStatus.Value, now))
                        throw new InvalidOperationException(
                            $"Booking {booking.Id} đổi trạng thái khi ghi nhận tiền về sau khi hủy; chờ cổng gửi lại kết quả.");
                    outcome = SuccessOutcome.LatePaymentRefund;
                }
                else
                {
                    outcome = SuccessOutcome.ExtraPayment;
                }
            }
            transaction.Complete();
        }

        payment.Status = PaymentTxStatus.SUCCESS;
        payment.PaidAt = now;
        switch (outcome)
        {
            case SuccessOutcome.Confirmed:
                booking.Status = BookingStatus.PAID_ONLINE;
                invoice.PaymentStatus = InvoicePaymentStatus.PARTIALLY_PAID;
                _logger.LogInformation("Thanh toán thành công {Amount} đ cho booking {BookingId}", payment.Amount, booking.Id);
                await _notifications.NotifyPaymentSucceededAsync(booking, payment, rawCancelToken!);
                break;

            case SuccessOutcome.LatePaymentRefund:
                booking.Status = BookingStatus.CANCELLED_PENDING_REFUND;
                _logger.LogWarning("Tiền về {Amount} đ sau khi booking {BookingId} đã hủy ({Status}); tạo hoàn 100 % tại chi nhánh",
                    payment.Amount, booking.Id, currentStatus);
                await _notifications.NotifyLatePaymentAsync(booking, payment.Amount);
                break;

            case SuccessOutcome.ExtraPayment:
                // Khoản thừa được trừ vào phần thu tại quầy khi check-out; đơn đã kết thúc thì phải hoàn tay tại chi nhánh
                var finished = currentStatus is BookingStatus.COMPLETED or BookingStatus.NO_SHOW;
                _logger.Log(finished ? LogLevel.Error : LogLevel.Warning,
                    "Booking {BookingId} ({Status}) nhận thêm {Amount} đ qua giao dịch {Ref}{Action}",
                    booking.Id, currentStatus, payment.Amount, payment.TransactionRef,
                    finished ? "; đơn đã kết thúc, cần hoàn tiền mặt tại chi nhánh" : "; trừ vào phần thu tại quầy khi check-out");
                break;
        }
    }

    // Giờ bắt đầu sớm nhất của đơn (giờ VN quy đổi UTC); đơn luôn có ít nhất một sân
    private static DateTime PlayStartUtc(Booking booking)
    {
        var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).FirstOrDefault()
            ?? throw new InvalidOperationException($"Booking {booking.Id} không có sân.");
        return DateTimeHelper.ToUtcFromVietnam(first.Date, first.StartTime);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
