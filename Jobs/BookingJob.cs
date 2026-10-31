using Hangfire;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Common.Constants;
using System.Diagnostics;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// Tất cả các tác vụ (Job) bảo trì đơn đặt sân theo lịch trình.
    ///
    /// Cơ chế Idempotency (Không trùng lặp):
    ///   Mỗi tác vụ lọc nghiêm ngặt theo trạng thái và thời gian, vì vậy việc chạy lại
    ///   một lô đã xử lý sẽ không gây tác dụng phụ (dữ liệu không còn khớp với điều kiện WHERE).
    ///
    /// Cơ chế Concurrency (Tránh chạy đồng thời):
    ///   Mỗi tác vụ được đánh dấu [DisableConcurrentExecution] để cơ chế khóa phân tán của Hangfire
    ///   ngăn chặn hai thực thể ứng dụng chạy cùng một tác vụ đồng thời. Thời gian chờ (10 giây)
    ///   được cấu hình ngắn — nếu không lấy được khóa, tác vụ sẽ bị bỏ qua và chờ lượt quét tiếp theo.
    ///
    /// Đổi trạng thái booking (P6-25, gói 6A-5):
    ///   Tác vụ 01, 02, 04 xử lý từng đơn trong một transaction riêng; câu cập nhật có điều kiện (hoặc khóa rồi đọc lại) chạy đầu tiên.
    ///   Đơn vừa được nhân viên check-in, check-out hay hủy thì job thua và bỏ qua, không ghi gì. Không SaveChanges cả lô entity đã nạp
    ///   từ trước (cách cũ ghi đè được thao tác vừa commit).
    ///
    /// Cơ chế giải phóng sân:
    ///   Sân chỉ được đặt về AVAILABLE khi không còn đơn đặt sân hoạt động nào khác giữ sân đó
    ///   ("hoạt động": CONFIRMED | PAID_ONLINE | IN_PROGRESS) và sân không ở SUSPENDED / INACTIVE.
    ///   "Sân còn bận" tính sau khi job thắng, chỉ loại trừ chính đơn đang xử lý.
    /// </summary>
    public class BookingJob : IBookingJob
    {
        // ── Khai báo Dependencies ───────────────────────────────────────────
        private readonly SmashCourtContext _db;
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly IBroadcastService _broadcast;
        private readonly ISlotInterestNotificationService _slotInterestNotifier;
        private readonly IBookingRepository _bookingRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IBookingCancellationService _cancellationService;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BookingJob> _logger;

        // ── Khai báo hằng số ────────────────────────────────────────────────

        /// <summary>
        /// Thời gian ân hạn (phút) trước khi đơn đặt sân đã xác nhận nhưng
        /// không check-in bị đánh dấu là NO_SHOW.
        /// </summary>
        private const int NoShowGraceMinutes = 15;

        /// <summary>Số đơn tối đa mỗi lần chạy của job hủy đơn hết hạn; còn thì lần chạy sau (mỗi phút) làm tiếp.</summary>
        private const int ExpiryBatchSize = 100;

        // ── Constructor ─────────────────────────────────────────────────────
        public BookingJob(
            SmashCourtContext db,
            ISlotInterestRepository slotInterestRepo,
            IBroadcastService broadcast,
            ISlotInterestNotificationService slotInterestNotifier,
            IBookingRepository bookingRepo,
            IInvoiceRepository invoiceRepo,
            ICourtRepository courtRepo,
            IBookingCancellationService cancellationService,
            TimeProvider timeProvider,
            ILogger<BookingJob> logger)
        {
            _db = db;
            _slotInterestRepo = slotInterestRepo;
            _broadcast = broadcast;
            _slotInterestNotifier = slotInterestNotifier;
            _bookingRepo = bookingRepo;
            _invoiceRepo = invoiceRepo;
            _courtRepo = courtRepo;
            _cancellationService = cancellationService;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 01 · Hủy các đơn PENDING quá hạn thanh toán (quét mỗi 1 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Hủy đơn PENDING quá hạn thanh toán (spec 7.11, gói 6A-5): tối đa 100 đơn mỗi lần, hạn sớm nhất trước. Mỗi đơn đi qua dịch vụ hủy
        /// chung trong transaction riêng: khóa booking, kiểm lại hạn (lần gia hạn của Thanh toán lại vừa commit thì bỏ qua), hủy có điều kiện
        /// (SYSTEM, PAYMENT_EXPIRED hoặc SYSTEM_CLEANUP), nhả sân và slot lock của đơn, Payment chờ ⇒ FAILED, hóa đơn ⇒ EXPIRED, thông báo
        /// sau commit. Slot lock hết hạn của toàn hệ thống do Tác vụ 03 dọn.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public Task CancelExpiredPendingBookingsAsync() =>
            ExpiryBatch.RunAsync("Job-01",
                () => _bookingRepo.GetExpiredUnpaidIdsAsync(UtcNow(), ExpiryBatchSize),
                _cancellationService.ExpireUnpaidAsync,
                _logger);

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 02 · Hoàn tất các đơn đặt sân đã kết thúc giờ chơi (quét mỗi 1 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tác vụ xử lý hoàn tất các đơn đặt sân khi hết giờ chơi (giờ kết thúc muộn nhất ≤ bây giờ).
        ///
        /// Mỗi đơn một transaction (B7, P6-25): khóa booking ⇒ đọc lại trạng thái booking và hóa đơn dưới khóa ⇒ đã đổi (nhân viên vừa
        /// check-out) thì bỏ qua ⇒ câu cập nhật có điều kiện ⇒ tắt booking_courts, trả sân ⇒ commit ⇒ SignalR.
        /// ┌────────────────┬──────────────────────────┬────────────────────────────────┐
        /// │ Trạng thái     │ Hóa đơn (đọc dưới khóa)  │ Kết quả trạng thái mới         │
        /// ├────────────────┼──────────────────────────┼────────────────────────────────┤
        /// │ IN_PROGRESS    │ PAID                     │ → COMPLETED                    │
        /// │ IN_PROGRESS    │ khác PAID / không có     │ → PENDING_PAYMENT              │
        /// │ CONFIRMED /    │ Đã check-in (dữ liệu cũ, │ Như IN_PROGRESS                │
        /// │ PAID_ONLINE    │ check-in nay luôn đổi    │                                │
        /// │                │ sang IN_PROGRESS)        │                                │
        /// │ CONFIRMED /    │ Chưa check-in            │ Bỏ qua (Tác vụ 04 xử lý)       │
        /// │ PAID_ONLINE    │                          │                                │
        /// └────────────────┴──────────────────────────┴────────────────────────────────┘
        /// Đơn trả cọc online để hóa đơn PARTIALLY_PAID nên sang PENDING_PAYMENT; hóa đơn chỉ đổi updated_at.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var now = UtcNow();
                var candidates = await _bookingRepo.GetPlayEndCandidatesAsync(TodayInVietnam(now));
                var ended = candidates.Where(b => b.BookingCourts.Count > 0 && LastPlayEndUtc(b) <= now).ToList();
                if (ended.Count == 0)
                {
                    _logger.LogInformation("[Job-02] Không có đơn nào hết giờ chơi. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                int completed = 0, pendingPayment = 0, skipped = 0, errors = 0;
                foreach (var booking in ended)
                {
                    try
                    {
                        var newStatus = await EndPlayAsync(booking, now);
                        if (newStatus == null)
                        {
                            skipped++;
                            _logger.LogInformation("[Job-02] Đơn {BookingId} đã đổi trạng thái trước khi job khóa được; bỏ qua", booking.Id);
                            continue;
                        }

                        if (newStatus == BookingStatus.COMPLETED)
                            completed++;
                        else
                            pendingPayment++;
                        await NotifyPlayEndedAsync(booking, now);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex, "[Job-02] Thất bại khi hoàn tất đơn {BookingId} (Trạng thái={Status}). Bỏ qua.",
                            booking.Id, booking.Status);
                    }
                }

                _logger.LogInformation(
                    "[Job-02] {Candidates} ứng viên hết giờ: hoàn tất {Completed}, chờ thanh toán {PendingPayment}, " +
                    "bỏ qua vì đổi trạng thái {Skipped}, lỗi {Errors}. Thời gian chạy: {Ms}ms",
                    ended.Count, completed, pendingPayment, skipped, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-02] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // Một đơn hết giờ trong transaction riêng; trả trạng thái mới, null khi đơn đã đổi (job thua)
        private async Task<BookingStatus?> EndPlayAsync(Booking booking, DateTime now)
        {
            BookingStatus newStatus;
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // Check-out và thêm dịch vụ cũng khóa booking trước nên dữ liệu đọc dưới khóa là bản cuối cùng
                await _bookingRepo.LockAsync(booking.Id);
                var current = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                if (current != booking.Status)
                    return null;

                var invoiceStatus = await _invoiceRepo.GetPaymentStatusByBookingIdAsync(booking.Id);
                if (invoiceStatus == null)
                    _logger.LogWarning("[Job-02] Đơn {BookingId} không có hóa đơn; chuyển về PENDING_PAYMENT", booking.Id);
                newStatus = invoiceStatus == InvoicePaymentStatus.PAID ? BookingStatus.COMPLETED : BookingStatus.PENDING_PAYMENT;

                if (!await _bookingRepo.EndPlayAsync(booking.Id, current, newStatus, now))
                    return null;

                await ReleaseCourtsAsync(booking, now);
                transaction.Complete();
            }

            booking.Status = newStatus;
            booking.UpdatedAt = now;
            return newStatus;
        }

        private async Task NotifyPlayEndedAsync(Booking booking, DateTime now)
        {
            try
            {
                var customerName = CustomerName(booking);
                var completed = booking.Status == BookingStatus.COMPLETED;
                var message = completed
                    ? $"Booking #{booking.BookingCode} của {customerName} đã hoàn tất."
                    : $"Booking #{booking.BookingCode} của {customerName} đã hết giờ chơi. Vui lòng thanh toán tại quầy.";

                // includeTimeGrid=false: hết giờ chơi không mở ra slot còn đặt được
                await _broadcast.BroadcastBookingEventAsync(
                    completed ? SignalREvents.BookingCompleted : SignalREvents.BookingPendingPayment,
                    BookingNotification(booking, message, now),
                    booking,
                    includeTimeGrid: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-02] Gửi thông báo của đơn {BookingId} lỗi; dữ liệu đã ghi không đổi", booking.Id);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 03 · Dọn dẹp các slot tạm khóa đã hết hiệu lực (quét mỗi 1 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tác vụ xóa các bản ghi SlotLock đã hết hạn giữ sân.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CleanupExpiredSlotLocksAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var now = DateTimeHelper.GetUtcNow();
                var deleted = await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)
                    .ExecuteDeleteAsync();

                if (deleted > 0)
                    _logger.LogInformation(
                        "[Job-03] Đã dọn dẹp {Count} slot tạm khóa hết hạn. Thời gian chạy: {Ms}ms",
                        deleted, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-03] Lỗi khi dọn dẹp slot tạm khóa. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 04 · Tự động phát hiện đơn không đến nhận sân (quét mỗi 5 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tác vụ quét các đơn CONFIRMED / PAID_ONLINE không check-in quá giờ bắt đầu + 15 phút ân hạn.
        /// Mỗi đơn một transaction (B6, P6-25): câu cập nhật có điều kiện "trạng thái chưa đổi và checked_in_at còn trống" chạy đầu tiên;
        /// thua (nhân viên vừa check-in hoặc vừa hủy) thì bỏ qua. Thắng thì tắt booking_courts, trả sân; sau commit phát BookingNoShow và
        /// báo người chờ slot qua dịch vụ chung (link trang chủ).
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task DetectNoShowBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var now = UtcNow();
                var candidates = await _bookingRepo.GetNoShowCandidatesAsync(TodayInVietnam(now));
                var noShows = candidates
                    .Where(b => b.BookingCourts.Count > 0 && FirstPlayStartUtc(b).AddMinutes(NoShowGraceMinutes) < now)
                    .ToList();
                if (noShows.Count == 0)
                {
                    _logger.LogInformation("[Job-04] Không có đơn nào vượt quá thời gian ân hạn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                int marked = 0, skipped = 0, errors = 0;
                foreach (var booking in noShows)
                {
                    try
                    {
                        if (!await MarkNoShowAsync(booking, now))
                        {
                            skipped++;
                            _logger.LogInformation(
                                "[Job-04] Đơn {BookingId} đã check-in hoặc đổi trạng thái trước khi job ghi; bỏ qua", booking.Id);
                            continue;
                        }

                        marked++;
                        LogNoShow(booking);
                        await NotifyNoShowAsync(booking, now);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex, "[Job-04] Lỗi khi đánh dấu đơn {BookingId} là NO_SHOW. Bỏ qua.", booking.Id);
                    }
                }

                _logger.LogInformation(
                    "[Job-04] {Candidates} ứng viên quá ân hạn: đã đánh NO_SHOW {Marked}, bỏ qua vì đổi trạng thái {Skipped}, lỗi {Errors}. " +
                    "Thời gian chạy: {Ms}ms",
                    noShows.Count, marked, skipped, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-04] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // Một đơn NO_SHOW trong transaction riêng; false khi job thua
        private async Task<bool> MarkNoShowAsync(Booking booking, DateTime now)
        {
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // Câu điều kiện đầu tiên vừa khóa dòng booking vừa quyết định bên thắng với check-in / hủy của nhân viên
                if (!await _bookingRepo.MarkNoShowAsync(booking.Id, booking.Status, now))
                    return false;

                await ReleaseCourtsAsync(booking, now);
                transaction.Complete();
            }

            booking.Status = BookingStatus.NO_SHOW;
            booking.UpdatedAt = now;
            return true;
        }

        private void LogNoShow(Booking booking)
        {
            var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).First();
            _logger.LogWarning(
                "[Job-04] CẢNH BÁO NO_SHOW — BookingId={BookingId} CustomerId={CustomerId} " +
                "Courts=[{CourtIds}] Ngày={Date} Giờ Bắt Đầu={Start} " +
                "Phương thức TT={Timing} Số tiền={Amount}",
                booking.Id,
                booking.CustomerId ?? Guid.Empty,
                string.Join(", ", booking.BookingCourts.Select(bc => bc.CourtId)),
                first.Date,
                first.StartTime,
                booking.Invoice?.PaymentTiming.ToString() ?? "N/A",
                booking.Invoice?.FinalTotal ?? 0);
        }

        // Mỗi bước tự bắt lỗi: thông báo hỏng không đổi kết quả đã ghi và không chặn bước sau
        private async Task NotifyNoShowAsync(Booking booking, DateTime now)
        {
            try
            {
                // includeTimeGrid=true: NO_SHOW nhả slot nên lưới giờ cần tải lại
                await _broadcast.BroadcastBookingEventAsync(
                    SignalREvents.BookingNoShow,
                    BookingNotification(booking,
                        $"Booking #{booking.BookingCode} của {CustomerName(booking)} bị đánh dấu NO_SHOW do không nhận sân đúng giờ.", now),
                    booking,
                    includeTimeGrid: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-04] Phát BookingNoShow của đơn {BookingId} lỗi; dữ liệu đã ghi không đổi", booking.Id);
            }

            try
            {
                await _slotInterestNotifier.NotifyReleasedSlotsAsync(booking);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-04] Báo người chờ slot của đơn {BookingId} lỗi; dữ liệu đã ghi không đổi", booking.Id);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 05 · Dọn dẹp đăng ký chờ slot sân hết hạn (quét mỗi 1 giờ)
        // ════════════════════════════════════════════════════════════════════

        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CleanupExpiredSlotInterestsAsync()
        {
            try
            {
                var deleted = await _slotInterestRepo.DeleteExpiredAsync();
                if (deleted > 0)
                    _logger.LogInformation("[Job-05] Đã dọn dẹp {Count} bản đăng ký chờ slot hết hạn.", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-05] Lỗi dọn dẹp slot interest.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Private helpers (Các hàm phụ trợ nội bộ)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tắt booking_courts của đơn và trả sân về AVAILABLE khi không còn đơn hoạt động khác giữ sân (tính sau khi job thắng, chỉ loại
        /// trừ chính đơn này); sân SUSPENDED / INACTIVE giữ nguyên. Gọi trong transaction của job, sau câu cập nhật có điều kiện.
        /// </summary>
        private async Task ReleaseCourtsAsync(Booking booking, DateTime now)
        {
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

            var courtIds = booking.BookingCourts.Select(bc => bc.CourtId).Distinct().ToList();
            var held = await _bookingRepo.GetCourtsHeldByOtherBookingsAsync(courtIds, booking.Id);
            var free = courtIds.Except(held).ToList();
            if (free.Count > 0)
                await _courtRepo.ReleaseToAvailableAsync(free, now);

            foreach (var bc in booking.BookingCourts)
                bc.IsActive = false;
        }

        private static BookingNotificationDto BookingNotification(Booking booking, string message, DateTime timestamp) => new()
        {
            BookingId = booking.Id,
            CustomerId = booking.CustomerId ?? Guid.Empty,
            CustomerName = CustomerName(booking),
            BranchId = booking.BranchId,
            BranchName = booking.Branch?.Name ?? "",
            CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
            Status = booking.Status.ToString(),
            Message = message,
            Timestamp = timestamp
        };

        private static string CustomerName(Booking booking) => booking.Customer?.FullName ?? booking.GuestName ?? "Khách";

        // Giờ bắt đầu sớm nhất / kết thúc muộn nhất của đơn (giờ VN quy đổi UTC)
        private static DateTime FirstPlayStartUtc(Booking booking)
        {
            var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).First();
            return DateTimeHelper.ToUtcFromVietnam(first.Date, first.StartTime);
        }

        private static DateTime LastPlayEndUtc(Booking booking)
        {
            var last = booking.BookingCourts.OrderByDescending(bc => bc.Date).ThenByDescending(bc => bc.EndTime).First();
            return DateTimeHelper.ToUtcFromVietnam(last.Date, last.EndTime);
        }

        private static DateOnly TodayInVietnam(DateTime nowUtc) => DateOnly.FromDateTime(DateTimeHelper.ToVietnamTime(nowUtc));

        private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    }
}
