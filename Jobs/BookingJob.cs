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
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using Microsoft.Extensions.Configuration;

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
    /// Cơ chế giải phóng sân:
    ///   Sân chỉ được đặt về AVAILABLE khi không còn đơn đặt sân hoạt động nào khác giữ sân đó.
    ///   "Hoạt động" bao gồm: CONFIRMED | PAID_ONLINE | IN_PROGRESS.
    ///   Đơn đặt sân đang được xử lý sẽ bị loại trừ khỏi kiểm tra sân bận để tránh tự khóa chính nó.
    /// </summary>
    public class BookingJob : IBookingJob
    {
        // ── Khai báo Dependencies ───────────────────────────────────────────
        private readonly SmashCourtContext _db;
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly IBroadcastService _broadcast;
        private readonly EmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BookingJob> _logger;

        // ── Khai báo hằng số ────────────────────────────────────────────────

        /// <summary>Các trạng thái xác định sân đang được sử dụng.</summary>
        private static readonly BookingStatus[] ActiveStatuses =
        [
            BookingStatus.CONFIRMED,
            BookingStatus.PAID_ONLINE,
            BookingStatus.IN_PROGRESS,
        ];

        /// <summary>
        /// Thời gian ân hạn (phút) trước khi đơn đặt sân đã xác nhận nhưng
        /// không check-in bị đánh dấu là NO_SHOW.
        /// </summary>
        private const int NoShowGraceMinutes = 15;

        // ── Constructor ─────────────────────────────────────────────────────
        public BookingJob(
            SmashCourtContext db,
            ISlotInterestRepository slotInterestRepo,
            IBroadcastService broadcast,
            EmailService emailService,
            IConfiguration configuration,
            ILogger<BookingJob> logger)
        {
            _db = db;
            _slotInterestRepo = slotInterestRepo;
            _broadcast = broadcast;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }



        private async Task NotifySlotInterestedUsersAsync(Booking booking)
        {
            var frontendUrl = _configuration["FrontendBaseUrl"] ?? "https://smashcourt.vn";
            var branchName = booking.Branch?.Name ?? string.Empty;

            foreach (var bc in booking.BookingCourts)
            {
                var interested = await _slotInterestRepo.GetOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                if (!interested.Any()) continue;

                _logger.LogInformation(
                    "[SLOT_INTEREST] Notifying {Count} users for released slot | Court={CourtId} | Date={Date} | Slot={Start}-{End}",
                    interested.Count, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                var courtName = bc.Court?.Name ?? "Sân";
                var bookingUrl = $"{frontendUrl}/booking?courtId={bc.CourtId}&date={bc.Date:yyyy-MM-dd}&start={bc.StartTime:HH:mm}&end={bc.EndTime:HH:mm}";

                foreach (var interest in interested)
                {
                    try
                    {
                        await _emailService.SendSlotAvailableNotificationAsync(
                            interest.Email,
                            courtName,
                            branchName,
                            bc.Date,
                            bc.StartTime,
                            bc.EndTime,
                            bookingUrl);
                    }
                    catch (Exception ex)
                    {
                        // Lỗi gửi email không block việc notify người khác
                        _logger.LogError(ex,
                            "[SLOT_INTEREST] Failed to send notification to {Email} for slot Court={CourtId}",
                            interest.Email, bc.CourtId);
                    }
                }

                // Xóa tất cả interests của slot này sau khi đã notify (one-shot)
                var deletedCount = await _slotInterestRepo.DeleteOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                _logger.LogInformation(
                    "[SLOT_INTEREST] Deleted {DeletedCount} slot interests after notification | Court={CourtId} | Date={Date} | Slot={Start}-{End}",
                    deletedCount, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 01 · Hủy các đơn PENDING quá hạn thanh toán (quét mỗi 1 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tác vụ hủy các đơn PENDING có hóa đơn quá hạn thanh toán.
        ///
        /// Các trường hợp xử lý:
        ///   • Trạng thái PENDING + Hóa đơn quá hạn → CANCELLED, giải phóng sân.
        ///   • Trạng thái PENDING + Chưa quá hạn → Bỏ qua không quét.
        ///   • Sân đang bị giữ bởi đơn khác → Giữ nguyên trạng thái sân (tránh giải phóng nhầm).
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task CancelExpiredPendingBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-01] Bắt đầu quét đơn PENDING quá hạn.");

            try
            {
                var now = DateTimeHelper.GetUtcNow();
                // 1. Tìm các hóa đơn quá hạn và chưa thanh toán
                var expiredInvoices = await _db.Invoices
                    .Include(i => i.Booking)
                        .ThenInclude(b => b.BookingCourts)
                            .ThenInclude(bc => bc.Court)
                    .Include(i => i.Booking.Branch)
                    .Include(i => i.Booking.Customer)
                    .Where(i => i.PaymentStatus == InvoicePaymentStatus.UNPAID
                             && i.ExpiresAt < now
                             && i.Booking.Status == BookingStatus.PENDING)
                    .ToListAsync();

                if (expiredInvoices.Count == 0)
                {
                    _logger.LogInformation("[Job-01] Không có đơn PENDING nào quá hạn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                // Trích xuất danh sách các đơn đặt sân quá hạn để xử lý
                var expiredBookings = expiredInvoices.Select(i => i.Booking).ToList();

                var cancelledCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var cancelledBookingIds = expiredBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(cancelledCourtIds, cancelledBookingIds);

                // 2. Duyệt qua hóa đơn để cập nhật hóa đơn và đơn đặt sân tương ứng
                foreach (var invoice in expiredInvoices)
                {
                    // Cập nhật trạng thái hóa đơn
                    invoice.PaymentStatus = InvoicePaymentStatus.EXPIRED;
                    invoice.UpdatedAt = now;

                    // Cập nhật trạng thái đơn đặt sân
                    var booking = invoice.Booking;
                    if (booking != null)
                    {
                        booking.Status = BookingStatus.CANCELLED;
                        booking.CancelledAt = now;
                        booking.CancelSource = CancelSourceEnum.SYSTEM;
                        booking.UpdatedAt = now;

                        ReleaseCourtSlots(booking.BookingCourts, busyCourtIds, now, deactivate: true);
                    }
                }

                // Xóa các khóa slot tạm thời đã hết hạn trong cùng một transaction
                await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)
                    .ExecuteDeleteAsync();

                await _db.SaveChangesAsync();

                // 3. Gửi thông báo real-time qua SignalR
                foreach (var booking in expiredBookings)
                {
                    var customerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách";
                    var notification = new BookingNotificationDto
                    {
                        BookingId = booking.Id,
                        CustomerId = booking.CustomerId ?? Guid.Empty,
                        CustomerName = customerName,
                        BranchId = booking.BranchId,
                        BranchName = booking.Branch?.Name ?? "",
                        CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                        Status = booking.Status.ToString(),
                        Message = $"Booking #{booking.BookingCode} của {customerName} đã bị hủy tự động do hết hạn thanh toán.",
                        Timestamp = DateTimeHelper.GetUtcNow()
                    };

                    // includeTimeGrid=true: slot released → customers viewing the grid need a refresh
                    await _broadcast.BroadcastBookingEventAsync(
                        SignalREvents.BookingExpired,
                        notification,
                        booking,
                        includeTimeGrid: true);

                    // Gửi email cho người dùng đăng ký quan tâm slot trống (nếu có)
                    await NotifySlotInterestedUsersAsync(booking);
                }

                _logger.LogInformation(
                    "[Job-01] Đã hủy {Count} đơn PENDING quá hạn thanh toán. Thời gian chạy: {Ms}ms",
                    expiredBookings.Count, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-01] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 02 · Hoàn tất các đơn đặt sân đã kết thúc giờ chơi (quét mỗi 1 phút)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tác vụ xử lý hoàn tất các đơn đặt sân khi hết giờ chơi.
        ///
        /// Các trường hợp xử lý:
        /// ┌────────────────┬──────────────────────────┬────────────────────────────────┐
        /// │ Trạng thái     │ Điều kiện hóa đơn        │ Kết quả trạng thái mới         │
        /// ├────────────────┼──────────────────────────┼────────────────────────────────┤
        /// │ IN_PROGRESS    │ Hóa đơn đã PAID          │ → COMPLETED                    │
        /// │ IN_PROGRESS    │ Hóa đơn khác PAID        │ → PENDING_PAYMENT              │
        /// │ PAID_ONLINE    │ Chưa check-in            │ Bỏ qua (Do Job-04 xử lý)       │
        /// │ PAID_ONLINE    │ Đã check-in + PAID       │ → COMPLETED                    │
        /// │ PAID_ONLINE    │ Đã check-in + PARTIALLY  │ → PENDING_PAYMENT              │
        /// │ CONFIRMED      │ Chưa check-in            │ Bỏ qua (Do Job-04 xử lý)       │
        /// │ CONFIRMED      │ Đã check-in + PAID       │ → COMPLETED                    │
        /// │ CONFIRMED      │ Đã check-in + khác PAID  │ → PENDING_PAYMENT              │
        /// └────────────────┴──────────────────────────┴────────────────────────────────┘
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-02] Bắt đầu quét đơn hoạt động hết giờ.");

            try
            {
                var now = DateTimeHelper.GetUtcNow();

                var activeBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Include(b => b.Branch)
                    .Include(b => b.Customer)
                    .Where(b =>
                        b.Status == BookingStatus.IN_PROGRESS ||
                        b.Status == BookingStatus.PAID_ONLINE ||
                        b.Status == BookingStatus.CONFIRMED)
                    .ToListAsync();

                if (activeBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-02] Không có đơn hoạt động nào cần xử lý. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                // Lọc danh sách các đơn đã hết giờ chơi trong bộ nhớ
                var expiredBookings = activeBookings
                    .Where(b => b.BookingCourts.Any() &&
                                ToUtcFromVietnam(
                                    b.BookingCourts.OrderByDescending(bc => bc.Date)
                                                   .ThenByDescending(bc => bc.EndTime)
                                                   .First().Date,
                                    b.BookingCourts.OrderByDescending(bc => bc.Date)
                                                   .ThenByDescending(bc => bc.EndTime)
                                                   .First().EndTime) <= now)
                    .ToList();

                if (expiredBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-02] Không có đơn nào hết giờ chơi. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var affectedCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var processingIds = expiredBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(affectedCourtIds, processingIds);

                int completed = 0;
                int pendingPayment = 0;
                int skipped = 0;
                int errors = 0;

                foreach (var booking in expiredBookings)
                {
                    try
                    {
                        var result = FinalizeExpiredBooking(booking, busyCourtIds, now);

                        switch (result)
                        {
                            case FinalizeResult.Completed: completed++; break;
                            case FinalizeResult.PendingPayment: pendingPayment++; break;
                            case FinalizeResult.Skipped: skipped++; break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex,
                            "[Job-02] Thất bại khi hoàn tất đơn {BookingId} (Trạng thái={Status}). Bỏ qua.",
                            booking.Id, booking.Status);
                        // Khôi phục lại trạng thái trong bộ nhớ để tránh lưu lỗi dữ liệu một phần
                        _db.Entry(booking).State = EntityState.Unchanged;
                        foreach (var bc in booking.BookingCourts)
                            _db.Entry(bc).State = EntityState.Unchanged;
                        if (booking.Invoice != null)
                            _db.Entry(booking.Invoice).State = EntityState.Unchanged;
                    }
                }

                await _db.SaveChangesAsync();

                // 3. Gửi thông báo real-time qua SignalR
                foreach (var booking in expiredBookings)
                {
                    if (booking.Status == BookingStatus.COMPLETED || booking.Status == BookingStatus.PENDING_PAYMENT)
                    {
                        var customerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách";
                        var notification = new BookingNotificationDto
                        {
                            BookingId = booking.Id,
                            CustomerId = booking.CustomerId ?? Guid.Empty,
                            CustomerName = customerName,
                            BranchId = booking.BranchId,
                            BranchName = booking.Branch?.Name ?? "",
                            CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                            Status = booking.Status.ToString(),
                            Message = booking.Status == BookingStatus.COMPLETED
                                ? $"Booking #{booking.BookingCode} của {customerName} đã hoàn tất."
                                : $"Booking #{booking.BookingCode} của {customerName} đã hết giờ chơi. Vui lòng thanh toán tại quầy.",
                            Timestamp = DateTimeHelper.GetUtcNow()
                        };

                        var eventName = booking.Status == BookingStatus.COMPLETED
                            ? SignalREvents.BookingCompleted
                            : SignalREvents.BookingPendingPayment; // PENDING_PAYMENT sử dụng BookingPendingPayment để phân biệt với Cancelled

                        // includeTimeGrid=false: session end doesn't open a currently bookable slot
                        await _broadcast.BroadcastBookingEventAsync(
                            eventName,
                            notification,
                            booking,
                            includeTimeGrid: false);
                    }
                }

                _logger.LogInformation(
                    "[Job-02] Hoàn tất quét. Đã xong={Completed} Chờ thanh toán={PendingPayment} " +
                    "Bỏ qua={Skipped} Lỗi={Errors} Thời gian chạy: {Ms}ms",
                    completed, pendingPayment, skipped, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-02] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Tác vụ 03 · Dọn dẹp các slot tạm khóa đã hết hiệu lực (quét mỗi 30 giây)
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
        /// Tác vụ quét các đơn CONFIRMED / PAID_ONLINE không check-in trong thời gian ân hạn.
        /// Chuyển trạng thái sang NO_SHOW và giải phóng sân trống.
        /// </summary>
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task DetectNoShowBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[Job-04] Bắt đầu quét phát hiện NO_SHOW.");

            try
            {
                var now = DateTimeHelper.GetUtcNow();
                var eligibleStatuses = BookingStatusTransition.GetNoShowEligibleStatuses();

                var candidates = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Include(b => b.Branch)
                    .Include(b => b.Customer)
                    .Where(b => eligibleStatuses.Contains(b.Status) && b.CheckedInAt == null)
                    .ToListAsync();

                if (candidates.Count == 0)
                {
                    _logger.LogInformation("[Job-04] Không phát hiện đơn tiềm năng NO_SHOW. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var noShowBookings = candidates
                    .Where(b =>
                    {
                        if (!b.BookingCourts.Any()) return false;
                        var first = b.BookingCourts
                            .OrderBy(bc => bc.Date)
                            .ThenBy(bc => bc.StartTime)
                            .First();
                        return ToUtcFromVietnam(first.Date, first.StartTime)
                                   .AddMinutes(NoShowGraceMinutes) < now;
                    })
                    .ToList();

                if (noShowBookings.Count == 0)
                {
                    _logger.LogInformation("[Job-04] Không có đơn nào vượt quá thời gian ân hạn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var affectedCourtIds = noShowBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var processingIds = noShowBookings
                    .Select(b => b.Id)
                    .ToHashSet();

                var busyCourtIds = await GetBusyCourtIdsAsync(affectedCourtIds, processingIds);

                int marked = 0;
                int errors = 0;

                foreach (var booking in noShowBookings)
                {
                    try
                    {
                        booking.Status = BookingStatus.NO_SHOW;
                        booking.UpdatedAt = now;

                        foreach (var bc in booking.BookingCourts)
                        {
                            bc.IsActive = false;

                            if (bc.Court != null
                                && !busyCourtIds.Contains(bc.CourtId)
                                && bc.Court.Status != CourtStatus.SUSPENDED
                                && bc.Court.Status != CourtStatus.INACTIVE)
                            {
                                bc.Court.Status = CourtStatus.AVAILABLE;
                                bc.Court.UpdatedAt = now;
                            }
                        }

                        marked++;

                        var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).First();
                        var courtIdList = string.Join(", ", booking.BookingCourts.Select(bc => bc.CourtId));

                        _logger.LogWarning(
                            "[Job-04] CẢNH BÁO NO_SHOW — BookingId={BookingId} CustomerId={CustomerId} " +
                            "Courts=[{CourtIds}] Ngày={Date} Giờ Bắt Đầu={Start} " +
                            "Phương thức TT={Timing} Số tiền={Amount}",
                            booking.Id,
                            booking.CustomerId ?? Guid.Empty,
                            courtIdList,
                            first.Date,
                            first.StartTime,
                            booking.Invoice?.PaymentTiming.ToString() ?? "N/A",
                            booking.Invoice?.FinalTotal ?? 0);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        _logger.LogError(ex,
                            "[Job-04] Lỗi khi đánh dấu đơn {BookingId} là NO_SHOW. Bỏ qua.",
                            booking.Id);
                        _db.Entry(booking).State = EntityState.Unchanged;
                        foreach (var bc in booking.BookingCourts)
                            _db.Entry(bc).State = EntityState.Unchanged;
                    }
                }

                await _db.SaveChangesAsync();

                // Gửi thông báo real-time qua SignalR cho các đơn NO_SHOW
                foreach (var booking in noShowBookings)
                {
                    var customerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách";
                    var notification = new BookingNotificationDto
                    {
                        BookingId = booking.Id,
                        CustomerId = booking.CustomerId ?? Guid.Empty,
                        CustomerName = customerName,
                        BranchId = booking.BranchId,
                        BranchName = booking.Branch?.Name ?? "",
                        CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                        Status = booking.Status.ToString(),
                        Message = $"Booking #{booking.BookingCode} của {customerName} bị đánh dấu NO_SHOW do không nhận sân đúng giờ.",
                        Timestamp = DateTimeHelper.GetUtcNow()
                    };

                    // includeTimeGrid=true: no-show releases the slot → timegrid needs refresh
                    await _broadcast.BroadcastBookingEventAsync(
                        SignalREvents.BookingNoShow,
                        notification,
                        booking,
                        includeTimeGrid: true);

                    // Gửi email cho người dùng đăng ký quan tâm slot trống (nếu có)
                    await NotifySlotInterestedUsersAsync(booking);
                }

                _logger.LogInformation(
                    "[Job-04] Kết thúc. Đã xử lý NO_SHOW={Marked} Lỗi={Errors} Thời gian chạy: {Ms}ms",
                    marked, errors, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Job-04] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
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
        /// Trả về danh sách ID sân hiện đang bận bởi các đơn đặt sân đang hoạt động khác.
        /// Tránh giải phóng nhầm sân khi kết thúc một đơn nhưng sân đang được đặt tiếp.
        /// </summary>
        private async Task<HashSet<Guid>> GetBusyCourtIdsAsync(
            HashSet<Guid> candidateCourtIds,
            HashSet<Guid> excludeBookingIds)
        {
            if (candidateCourtIds.Count == 0) return [];

            var ids = await _db.BookingCourts
                .Where(other =>
                    candidateCourtIds.Contains(other.CourtId) &&
                    other.IsActive &&
                    !excludeBookingIds.Contains(other.BookingId) &&
                    ActiveStatuses.Contains(other.Booking.Status))
                .Select(other => other.CourtId)
                .Distinct()
                .ToListAsync();

            return ids.ToHashSet();
        }

        /// <summary>
        /// Hủy kích hoạt BookingCourt và đặt trạng thái sân về trống (AVAILABLE) nếu an toàn.
        /// </summary>
        private static void ReleaseCourtSlots(
            IEnumerable<BookingCourt> slots,
            HashSet<Guid> busyCourtIds,
            DateTime now,
            bool deactivate)
        {
            foreach (var bc in slots)
            {
                if (deactivate) bc.IsActive = false;

                if (bc.Court != null
                    && !busyCourtIds.Contains(bc.CourtId)
                    && bc.Court.Status != CourtStatus.SUSPENDED
                    && bc.Court.Status != CourtStatus.INACTIVE)
                {
                    bc.Court.Status = CourtStatus.AVAILABLE;
                    bc.Court.UpdatedAt = now;
                }
            }
        }

        /// <summary>
        /// Logic lõi xử lý hoàn tất cho từng đơn hết giờ của Job-02.
        /// </summary>
        private FinalizeResult FinalizeExpiredBooking(
            Booking booking,
            HashSet<Guid> busyCourtIds,
            DateTime now)
        {
            if (!booking.BookingCourts.Any())
            {
                _logger.LogWarning(
                    "[Job-02] Đơn đặt {BookingId} không chứa BookingCourts. Bỏ qua.",
                    booking.Id);
                return FinalizeResult.Skipped;
            }

            var invoice = booking.Invoice;

            switch (booking.Status)
            {
                case BookingStatus.PAID_ONLINE:
                case BookingStatus.CONFIRMED:
                    {
                        if (booking.CheckedInAt == null)
                            return FinalizeResult.Skipped;

                        return CompleteOrPendPayment(booking, invoice, busyCourtIds, now);
                    }

                case BookingStatus.IN_PROGRESS:
                    {
                        return CompleteOrPendPayment(booking, invoice, busyCourtIds, now);
                    }

                default:
                    _logger.LogWarning(
                        "[Job-02] Trạng thái không mong muốn {Status} trên đơn {BookingId}.",
                        booking.Status, booking.Id);
                    return FinalizeResult.Skipped;
            }
        }

        /// <summary>
        /// Chuyển đơn sang COMPLETED hoặc PENDING_PAYMENT dựa trên hóa đơn, sau đó giải phóng sân.
        /// </summary>
        private FinalizeResult CompleteOrPendPayment(
            Booking booking,
            Invoice? invoice,
            HashSet<Guid> busyCourtIds,
            DateTime now)
        {
            if (invoice == null)
            {
                _logger.LogWarning(
                    "[Job-02] Đơn đặt {BookingId} (Trạng thái={Status}) không có hóa đơn. " +
                    "Mặc định chuyển về PENDING_PAYMENT.",
                    booking.Id, booking.Status);
            }

            bool fullyPaid = invoice?.PaymentStatus == InvoicePaymentStatus.PAID;

            booking.Status = fullyPaid ? BookingStatus.COMPLETED : BookingStatus.PENDING_PAYMENT;
            booking.UpdatedAt = now;

            if (invoice != null) invoice.UpdatedAt = now;

            foreach (var bc in booking.BookingCourts)
            {
                bc.IsActive = false;

                if (bc.Court != null
                    && !busyCourtIds.Contains(bc.CourtId)
                    && bc.Court.Status != CourtStatus.SUSPENDED
                    && bc.Court.Status != CourtStatus.INACTIVE)
                {
                    bc.Court.Status = CourtStatus.AVAILABLE;
                    bc.Court.UpdatedAt = now;
                }
            }

            return fullyPaid ? FinalizeResult.Completed : FinalizeResult.PendingPayment;
        }

        /// <summary>
        /// Chuyển đổi cặp ngày+giờ từ múi giờ Việt Nam sang UTC.
        /// </summary>
        private static DateTime ToUtcFromVietnam(DateOnly date, TimeOnly time)
        {
            var local = date.ToDateTime(time);
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                DateTimeHelper.VNTimezone);
        }
    }

    /// <summary>Kết quả xử lý hoàn tất để tổng hợp báo cáo log.</summary>
    internal enum FinalizeResult
    {
        Completed,
        PendingPayment,
        Skipped,
    }
}