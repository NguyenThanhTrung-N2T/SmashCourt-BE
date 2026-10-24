using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Factories;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Thông báo sau commit của luồng đặt online và duyệt cọc (sự kiện SignalR, email). Mỗi bước tự bắt lỗi và chỉ ghi log:
/// thông báo hỏng không được làm hỏng kết quả đã lưu.
/// </summary>
public class BookingNotificationService : IBookingNotificationService
{
    private readonly IBroadcastService _broadcast;
    private readonly EmailService _emailService;
    private readonly ISlotInterestNotificationService _slotInterestNotifier;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingNotificationService> _logger;

    public BookingNotificationService(
        IBroadcastService broadcast,
        EmailService emailService,
        ISlotInterestNotificationService slotInterestNotifier,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BookingNotificationService> logger)
    {
        _broadcast = broadcast;
        _emailService = emailService;
        _slotInterestNotifier = slotInterestNotifier;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ── Đặt sân online ─────────────────────────────────────────────────────

    public async Task NotifyOnlineBookingAsync(Booking booking, DepositBranch branch, string? rawCancelToken)
    {
        if (rawCancelToken != null)
            await RunSafelyAsync("ConfirmationEmail", booking.Id, () => SendConfirmationEmailAsync(booking, rawCancelToken));
        else if (branch == DepositBranch.NeedApproval)
            await RunSafelyAsync("PendingApprovalEmail", booking.Id, () => SendPendingApprovalEmailAsync(booking));

        await RunSafelyAsync("OnlineBookingEvents", booking.Id, () => PublishOnlineBookingEventsAsync(booking, branch));
    }

    private async Task PublishOnlineBookingEventsAsync(Booking booking, DepositBranch branch)
    {
        var customerName = CustomerName(booking);
        var timestamp = UtcNow();
        var notification = BookingNotification(booking, OnlineBookingEventMessage(booking.BookingCode, customerName, booking.Status), timestamp);

        if (branch == DepositBranch.Rejected)
        {
            // Khách đã nhận kết quả ở response; chỉ nhân viên và lưới giờ cần biết slot trống lại
            await _broadcast.BroadcastBookingEventToStaffAsync(SignalREvents.BookingCancelled, notification, booking, includeTimeGrid: true);
            return;
        }

        await _broadcast.BroadcastBookingEventAsync(SignalREvents.BookingCreated, notification, booking, includeTimeGrid: true);
        if (branch != DepositBranch.NeedApproval)
            return;

        await _broadcast.BroadcastBookingPendingApprovalAsync(new BookingPendingApprovalNotificationDto
        {
            BookingId = booking.Id,
            BookingCode = booking.BookingCode,
            BranchId = booking.BranchId,
            BranchName = booking.Branch?.Name ?? string.Empty,
            CustomerName = customerName,
            CourtIds = notification.CourtIds,
            Status = booking.Status.ToString(),
            ApprovalExpiresAt = booking.ApprovalExpiresAt,
            Message = $"Booking #{booking.BookingCode} của {customerName} đang chờ duyệt cọc.",
            Timestamp = timestamp
        });
        await _broadcast.BroadcastPendingApprovalQueueChangedAsync(
            QueueChanged(booking, SignalREvents.QueueChangeAdded, SignalREvents.QueueReasonNew, timestamp));
    }

    // Câu thông báo theo trạng thái cuối của đơn, cùng nhãn với FE
    private static string OnlineBookingEventMessage(string bookingCode, string customerName, BookingStatus status) => status switch
    {
        BookingStatus.PENDING_APPROVAL => $"Booking #{bookingCode} của {customerName} đã được tạo (Chờ duyệt cọc).",
        BookingStatus.CONFIRMED => $"Booking #{bookingCode} của {customerName} đã được tạo (Đã xác nhận, thanh toán tại quầy).",
        BookingStatus.CANCELLED => $"Booking #{bookingCode} của {customerName} không được nhận theo chính sách, slot đã được giải phóng.",
        _ => $"Booking #{bookingCode} của {customerName} đã được tạo (Chờ thanh toán)."
    };

    // ── Duyệt / từ chối ────────────────────────────────────────────────────

    public async Task NotifyApprovalResolvedAsync(Booking booking, bool rejected, string? rawCancelToken)
    {
        var timestamp = UtcNow();
        var message = rejected
            ? $"Booking #{booking.BookingCode} không được chi nhánh nhận."
            : booking.Status == BookingStatus.CONFIRMED
                ? $"Booking #{booking.BookingCode} đã được duyệt, đơn đã xác nhận, thanh toán tại quầy."
                : $"Booking #{booking.BookingCode} đã được duyệt. Vui lòng thanh toán tiền cọc.";

        await RunSafelyAsync("BookingApprovalResolved", booking.Id, () => _broadcast.BroadcastBookingApprovalResolvedAsync(
            new BookingApprovalResolvedNotificationDto
            {
                BookingId = booking.Id,
                BookingCode = booking.BookingCode,
                BranchId = booking.BranchId,
                ApprovalResult = rejected ? nameof(ApprovalResult.REJECTED) : nameof(ApprovalResult.APPROVED),
                Status = booking.Status.ToString(),
                Message = message,
                Timestamp = timestamp
            }, booking.CustomerId));

        await RunSafelyAsync("PendingApprovalQueueChanged", booking.Id, () => _broadcast.BroadcastPendingApprovalQueueChangedAsync(
            QueueChanged(booking, SignalREvents.QueueChangeRemoved,
                rejected ? SignalREvents.QueueReasonRejected : SignalREvents.QueueReasonApproved, timestamp)));

        if (rejected)
        {
            // Slot trống lại: lưới giờ và người đăng ký chờ slot cần biết
            await RunSafelyAsync("BookingCancelled", booking.Id, () => _broadcast.BroadcastBookingEventAsync(
                SignalREvents.BookingCancelled, BookingNotification(booking, message, timestamp), booking, includeTimeGrid: true));
            await RunSafelyAsync("SlotInterest", booking.Id, () => _slotInterestNotifier.NotifyReleasedSlotsAsync(booking));
        }

        await RunSafelyAsync("ApprovalEmail", booking.Id, () => SendApprovalEmailAsync(booking, rejected, rawCancelToken));
    }

    private Task SendApprovalEmailAsync(Booking booking, bool rejected, string? rawCancelToken)
    {
        if (rejected)
            return SendIfCustomerHasEmailAsync(booking, url => _emailService.SendApprovalRejectedAsync(BookingEmailFactory.BuildApproval(booking, url)));
        // Duyệt 0 %: đơn đã xác nhận nên gửi email xác nhận kèm link hủy như đơn thu tại quầy
        if (rawCancelToken != null)
            return SendConfirmationEmailAsync(booking, rawCancelToken);
        return SendIfCustomerHasEmailAsync(booking, url => _emailService.SendDepositApprovedAsync(BookingEmailFactory.BuildApproval(booking, url)));
    }

    // ── Email dùng chung ───────────────────────────────────────────────────

    private Task SendConfirmationEmailAsync(Booking booking, string rawCancelToken) =>
        SendIfCustomerHasEmailAsync(booking, url => _emailService.SendBookingConfirmationAsync(BookingEmailFactory.Build(booking, rawCancelToken, url)));

    private Task SendPendingApprovalEmailAsync(Booking booking) =>
        SendIfCustomerHasEmailAsync(booking, url => _emailService.SendBookingPendingApprovalAsync(BookingEmailFactory.BuildApproval(booking, url)));

    private Task SendIfCustomerHasEmailAsync(Booking booking, Func<string, Task> send)
    {
        if (string.IsNullOrWhiteSpace(booking.Customer?.Email))
        {
            _logger.LogWarning("Không gửi email cho booking {BookingId}: khách không có email", booking.Id);
            return Task.CompletedTask;
        }
        return send(_configuration["FrontendBaseUrl"] ?? "http://localhost:3000");
    }

    // ── Phụ trợ ────────────────────────────────────────────────────────────

    private static BookingNotificationDto BookingNotification(Booking booking, string message, DateTime timestamp) => new()
    {
        BookingId = booking.Id,
        CustomerId = booking.CustomerId ?? Guid.Empty,
        CustomerName = CustomerName(booking),
        BranchId = booking.BranchId,
        BranchName = booking.Branch?.Name ?? string.Empty,
        CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
        Status = booking.Status.ToString(),
        Message = message,
        Timestamp = timestamp
    };

    private static PendingApprovalQueueChangedNotificationDto QueueChanged(Booking booking, string change, string reason, DateTime timestamp) => new()
    {
        BranchId = booking.BranchId,
        BookingId = booking.Id,
        Change = change,
        Reason = reason,
        Timestamp = timestamp
    };

    private static string CustomerName(Booking booking) => booking.Customer?.FullName ?? booking.GuestName ?? "Khách";

    private async Task RunSafelyAsync(string step, Guid bookingId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bước thông báo '{Step}' của booking {BookingId} lỗi; dữ liệu đã ghi không đổi", step, bookingId);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
