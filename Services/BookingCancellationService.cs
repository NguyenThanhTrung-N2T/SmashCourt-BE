using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Cancellation;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.Cancellation;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Một nơi cho mọi đường hủy của người dùng: kiểm quyền và trạng thái theo từng đường, rồi chạy chung các bước hủy, nhả sân, hoàn tiền
/// (tiền mặt tại chi nhánh), trả lượt khuyến mãi và thông báo.
/// </summary>
public class BookingCancellationService : IBookingCancellationService
{
    private static readonly BookingStatus[] StaffCancellable =
        [BookingStatus.PENDING, BookingStatus.PENDING_APPROVAL, BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];
    private static readonly BookingStatus[] CustomerCancellable =
        [BookingStatus.PENDING, BookingStatus.PENDING_APPROVAL, BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];
    private static readonly BookingStatus[] LinkCancellable = [BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];

    private readonly IBookingRepository _bookingRepo;
    private readonly ISlotLockRepository _slotLockRepo;
    private readonly ICourtRepository _courtRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IRefundRepository _refundRepo;
    private readonly IPromotionRepository _promotionRepo;
    private readonly ICancelPolicyRepository _cancelPolicyRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IBookingNotificationService _notifications;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingCancellationService> _logger;

    public BookingCancellationService(
        IBookingRepository bookingRepo,
        ISlotLockRepository slotLockRepo,
        ICourtRepository courtRepo,
        IPaymentRepository paymentRepo,
        IInvoiceRepository invoiceRepo,
        IRefundRepository refundRepo,
        IPromotionRepository promotionRepo,
        ICancelPolicyRepository cancelPolicyRepo,
        IUserBranchRepository userBranchRepo,
        IBookingNotificationService notifications,
        TimeProvider timeProvider,
        ILogger<BookingCancellationService> logger)
    {
        _bookingRepo = bookingRepo;
        _slotLockRepo = slotLockRepo;
        _courtRepo = courtRepo;
        _paymentRepo = paymentRepo;
        _invoiceRepo = invoiceRepo;
        _refundRepo = refundRepo;
        _promotionRepo = promotionRepo;
        _cancelPolicyRepo = cancelPolicyRepo;
        _userBranchRepo = userBranchRepo;
        _notifications = notifications;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task CancelByStaffAsync(Guid bookingId, Guid staffId, string role)
    {
        var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId)
            ?? throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

        // OWNER thao tác mọi chi nhánh; nhân viên và quản lý chỉ chi nhánh được gán
        if (role != nameof(UserRole.OWNER) && !await _userBranchRepo.IsUserInBranchAsync(staffId, booking.BranchId))
            throw new AppException(403, "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);

        // Đơn đang chơi phải check-out sớm, không hủy
        if (!StaffCancellable.Contains(booking.Status))
            throw new AppException(400, "Không thể hủy đơn ở trạng thái này", ErrorCodes.BadRequest);

        await CancelAsync(booking, CancelSourceEnum.STAFF, BookingCancelReason.STAFF_CANCELLED, staffId, consumeTokenHash: null);
    }

    public async Task CancelByCustomerAsync(Guid bookingId, Guid customerId)
    {
        var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId)
            ?? throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

        if (booking.CustomerId != customerId)
            throw new AppException(403, "Bạn không có quyền hủy đơn này", ErrorCodes.Forbidden);
        EnsureAccountNotLocked(booking, "Tài khoản bị khóa, vui lòng liên hệ nhân viên");

        // Bấm hủy hai lần không báo lỗi
        if (BookingStatusTransition.GetCancelledStatuses().Contains(booking.Status))
            return;

        EnsureNotStarted(booking);
        if (!CustomerCancellable.Contains(booking.Status))
            throw new AppException(400, "Đơn đặt sân không thể hủy ở trạng thái hiện tại", ErrorCodes.BadRequest);

        // Hủy trong app thì link hủy trong email cũng hết dùng được
        await CancelAsync(booking, CancelSourceEnum.CUSTOMER, BookingCancelReason.CUSTOMER_CANCELLED, cancelledBy: null,
            consumeTokenHash: booking.CancelTokenHash, tokenMustBeUnused: false);
    }

    public async Task CancelByTokenAsync(string token)
    {
        var tokenHash = CancelTokenHelper.Hash(token);
        var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash)
            ?? throw new AppException(404, "Link hủy không hợp lệ", ErrorCodes.NotFound);

        EnsureAccountNotLocked(booking, "Tài khoản bị khóa, vui lòng liên hệ nhân viên");
        if (BookingStatusTransition.GetCancelledStatuses().Contains(booking.Status))
            return;

        // Kiểm hết trước khi tiêu token: lỗi ở bước nào cũng không được làm chết link
        if (booking.CancelTokenUsedAt.HasValue)
            throw new AppException(400, "Link hủy đã được sử dụng", ErrorCodes.BadRequest);
        if (booking.CancelTokenExpiresAt < UtcNow())
            throw new AppException(400, "Link hủy đã hết hạn", ErrorCodes.BadRequest);
        EnsureNotStarted(booking);
        if (!LinkCancellable.Contains(booking.Status))
            throw new AppException(400, "Đơn đặt sân không thể hủy ở trạng thái hiện tại", ErrorCodes.BadRequest);

        await CancelAsync(booking, CancelSourceEnum.LINK, BookingCancelReason.CUSTOMER_CANCELLED, cancelledBy: null,
            consumeTokenHash: tokenHash, tokenMustBeUnused: true);
    }

    public async Task<CancelTokenInfoDto> GetCancelInfoAsync(string token)
    {
        var booking = await _bookingRepo.GetByCancelTokenAsync(CancelTokenHelper.Hash(token))
            ?? throw new AppException(404, "Link hủy không hợp lệ hoặc đã hết hạn", ErrorCodes.NotFound);

        if (booking.CancelTokenUsedAt.HasValue)
            throw new AppException(400, "Link hủy đã được sử dụng", ErrorCodes.BadRequest);
        if (booking.CancelTokenExpiresAt < UtcNow())
            throw new AppException(400, "Link hủy đã hết hạn", ErrorCodes.BadRequest);
        EnsureAccountNotLocked(booking, "Tài khoản bị khóa, vui lòng liên hệ nhân viên để được hỗ trợ");

        var firstCourt = FirstCourt(booking);
        var quote = await QuoteRefundAsync(booking, UtcNow());

        return new CancelTokenInfoDto
        {
            BookingId = booking.Id,
            BranchName = booking.Branch.Name,
            CourtNames = booking.BookingCourts
                .Select(bc => bc.Court?.Name ?? string.Empty)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList(),
            BookingDate = booking.BookingDate.ToDateTime(TimeOnly.MinValue),
            StartTime = firstCourt.StartTime.ToTimeSpan(),
            EndTime = firstCourt.EndTime.ToTimeSpan(),
            RefundAmount = quote.Amount,
            RefundPercent = quote.Percent,
            Status = booking.Status.ToString()
        };
    }

    // ── Hủy do hết hạn (job) ────────────────────────────────────────────────

    public Task<bool> ExpireUnpaidAsync(Guid bookingId) => ExpireAsync(bookingId, BookingStatus.PENDING);

    public Task<bool> ExpireApprovalAsync(Guid bookingId) => ExpireAsync(bookingId, BookingStatus.PENDING_APPROVAL);

    // Đường riêng của job, không qua CancelAsync: thua thì trả false thay vì 409, khóa rồi kiểm lại hạn, hóa đơn sang EXPIRED,
    // không hoàn tiền và không trả lượt khuyến mãi (đơn chờ chưa có tiền về, chưa được tính lượt), thông báo hết hạn thay vì thông báo hủy
    private async Task<bool> ExpireAsync(Guid bookingId, BookingStatus expectedStatus)
    {
        var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);
        if (booking == null || booking.Status != expectedStatus)
            return false;

        // IPN đổi trạng thái đơn trong cùng transaction với Payment SUCCESS, nên đơn chờ có tiền về là dữ liệu hỏng: không hủy, để người xem
        if (booking.Invoice?.Payments.Any(p => p.Status == PaymentTxStatus.SUCCESS) == true)
        {
            _logger.LogError(
                "Booking {BookingId} đang {Status} nhưng đã có Payment SUCCESS; job hết hạn bỏ qua, cần kiểm tra dữ liệu",
                bookingId, booking.Status);
            return false;
        }

        var approvalExpiry = expectedStatus == BookingStatus.PENDING_APPROVAL;
        var reason = approvalExpiry ? BookingCancelReason.APPROVAL_EXPIRED : CancellationPolicy.PaymentExpiryReason(booking.Invoice);
        var now = UtcNow();

        using (var transaction = TransactionScopeHelper.ReadCommitted())
        {
            // Khóa trước rồi mới kiểm hạn: lần gia hạn của Thanh toán lại (hoặc lần duyệt) vừa commit thì job thấy hạn mới và bỏ qua
            await _bookingRepo.LockAsync(bookingId);
            var windowOver = approvalExpiry
                ? await _bookingRepo.IsApprovalWindowOverAsync(bookingId, now)
                : await _bookingRepo.IsPaymentWindowOverAsync(bookingId, now);
            if (!windowOver)
            {
                _logger.LogInformation("Booking {BookingId} không còn quá hạn khi đã khóa; job hết hạn bỏ qua", bookingId);
                return false;
            }

            var won = await _bookingRepo.CancelWithStatusCheckAsync(
                bookingId, [expectedStatus], BookingStatus.CANCELLED, CancelSourceEnum.SYSTEM, reason, cancelledBy: null, now) == 1;
            if (!won)
            {
                _logger.LogInformation("Booking {BookingId} đã đổi trạng thái; job hết hạn bỏ qua", bookingId);
                return false;
            }

            await ReleaseCourtsAsync(booking, now);

            if (booking.Invoice != null)
            {
                await _paymentRepo.FailPendingByInvoiceAsync(booking.Invoice.Id, exceptPaymentId: null, now);
                await _invoiceRepo.SetPaymentStatusAsync(booking.Invoice.Id, InvoicePaymentStatus.EXPIRED, now);
            }

            transaction.Complete();
        }

        booking.Status = BookingStatus.CANCELLED;
        booking.CancelledAt = now;
        booking.CancelSource = CancelSourceEnum.SYSTEM;
        booking.CancelReason = reason;
        booking.CancelledBy = null;
        if (booking.Invoice != null)
            booking.Invoice.PaymentStatus = InvoicePaymentStatus.EXPIRED;

        _logger.LogInformation("Booking {BookingId} đã hủy do hết hạn ({Reason}) từ {PreviousStatus}", bookingId, reason, expectedStatus);

        try
        {
            await _notifications.NotifyExpiredAsync(booking, expectedStatus);
        }
        catch (Exception ex)
        {
            // Đơn đã hủy xong; thông báo hỏng không đổi kết quả của job
            _logger.LogError(ex, "Thông báo hết hạn của booking {BookingId} lỗi; dữ liệu đã ghi không đổi", bookingId);
        }
        return true;
    }

    // ── Các bước chung ──────────────────────────────────────────────────────

    private async Task CancelAsync(
        Booking booking, CancelSourceEnum source, BookingCancelReason reason, Guid? cancelledBy,
        string? consumeTokenHash, bool tokenMustBeUnused = false)
    {
        var now = UtcNow();
        var previousStatus = booking.Status;
        // Tiền hoàn tính từ dữ liệu vừa đọc; câu cập nhật dưới chỉ thắng khi trạng thái chưa đổi nên số tiền vẫn đúng
        var quote = await QuoteRefundAsync(booking, now);
        var newStatus = quote.Amount > 0 ? BookingStatus.CANCELLED_PENDING_REFUND : BookingStatus.CANCELLED;

        using (var transaction = TransactionScopeHelper.ReadCommitted())
        {
            if (consumeTokenHash != null)
            {
                var consumed = await _bookingRepo.TryConsumeTokenAsync(booking.Id, consumeTokenHash, now);
                if (tokenMustBeUnused && !consumed)
                    throw new AppException(400, "Link hủy đã được sử dụng", ErrorCodes.BadRequest);
            }

            var won = await _bookingRepo.CancelWithStatusCheckAsync(
                booking.Id, [previousStatus], newStatus, source, reason, cancelledBy, now) == 1;
            if (!won)
                throw new AppException(409, "Đơn đã được xử lý bởi người khác", ErrorCodes.Conflict);

            await ReleaseCourtsAsync(booking, now);

            if (booking.Invoice != null)
                await _paymentRepo.FailPendingByInvoiceAsync(booking.Invoice.Id, exceptPaymentId: null, now);

            foreach (var share in quote.Shares)
                await _refundRepo.CreateAsync(new Refund
                {
                    PaymentId = share.PaymentId,
                    Amount = share.Amount,
                    RefundPercent = quote.Percent,
                    Status = RefundStatus.PENDING,
                    CreatedAt = now
                });

            if (booking.BookingPromotion != null && booking.Invoice != null
                && CancellationPolicy.ShouldReleasePromotionUse(booking.Invoice))
                await _promotionRepo.DecrementUsageCountAsync(booking.BookingPromotion.PromotionId);

            transaction.Complete();
        }

        booking.Status = newStatus;
        booking.CancelledAt = now;
        booking.CancelSource = source;
        booking.CancelReason = reason;
        booking.CancelledBy = cancelledBy;

        _logger.LogInformation(
            "Booking {BookingId} đã hủy ({Source}, {Reason}) từ {PreviousStatus}; hoàn {RefundAmount} đ tại chi nhánh",
            booking.Id, source, reason, previousStatus, quote.Amount);

        await _notifications.NotifyCancelledAsync(booking, previousStatus, quote.Amount);
    }

    // Sân về AVAILABLE chỉ khi không còn đơn khác đang giữ và sân không ở trạng thái đặc biệt
    private async Task ReleaseCourtsAsync(Booking booking, DateTime now)
    {
        await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);
        await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

        var releasable = new List<Guid>();
        foreach (var bookingCourt in booking.BookingCourts.Where(bc => bc.Court != null))
        {
            if (bookingCourt.Court!.Status is CourtStatus.SUSPENDED or CourtStatus.IN_USE or CourtStatus.INACTIVE)
                continue;

            var active = await _bookingRepo.GetActiveByCourtAndDateAsync(bookingCourt.CourtId, booking.BookingDate);
            if (active.All(bc => bc.BookingId == booking.Id))
                releasable.Add(bookingCourt.CourtId);
        }

        if (releasable.Count > 0)
            await _courtRepo.BatchUpdateStatusAsync(releasable.Distinct().ToList(), CourtStatus.AVAILABLE, now);
    }

    private async Task<RefundQuote> QuoteRefundAsync(Booking booking, DateTime nowUtc)
    {
        var firstCourt = FirstCourt(booking);
        var policies = await _cancelPolicyRepo.GetAllAsync();
        var playStartUtc = DateTimeHelper.ToUtcFromVietnam(booking.BookingDate, firstCourt.StartTime);
        return CancellationPolicy.QuoteRefund(policies, playStartUtc, nowUtc, booking.Invoice?.Payments ?? []);
    }

    private void EnsureNotStarted(Booking booking)
    {
        if (CancellationPolicy.HasStarted(booking.BookingDate, FirstCourt(booking).StartTime, UtcNow()))
            throw new AppException(400, "Đơn đã tới giờ chơi, không thể tự hủy; vui lòng liên hệ chi nhánh",
                ErrorCodes.BookingAlreadyStarted);
    }

    private static void EnsureAccountNotLocked(Booking booking, string message)
    {
        if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
            throw new AppException(403, message, ErrorCodes.AccountLocked);
    }

    // Các sân của một đơn luôn chung ngày và khung giờ
    private static BookingCourt FirstCourt(Booking booking) =>
        booking.BookingCourts.OrderBy(bc => bc.StartTime).FirstOrDefault()
        ?? throw new AppException(500, "Booking không có sân", ErrorCodes.InternalError);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
