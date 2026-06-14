using Microsoft.AspNetCore.SignalR;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Hubs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <inheritdoc cref="IBroadcastService"/>
public class BroadcastService : IBroadcastService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<BroadcastService> _logger;

    public BroadcastService(
        IHubContext<NotificationHub> hubContext,
        ILogger<BroadcastService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task BroadcastBookingEventAsync(
        string eventName,
        BookingNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false)
    {
        try
        {
            var tasks = new List<Task>
            {
                // Group theo cá nhân: user_{CustomerId}
                _hubContext.Clients
                    .Group($"user_{booking.CustomerId}")
                    .SendAsync(eventName, notification),

                // Group theo chi nhánh: branch_{BranchId}
                _hubContext.Clients
                    .Group($"branch_{booking.BranchId}")
                    .SendAsync(eventName, notification),

                // Group theo quyền: role_OWNER
                _hubContext.Clients
                    .Group($"role_{Models.Enums.UserRole.OWNER}")
                    .SendAsync(eventName, notification),
            };

            if (includeTimeGrid)
                tasks.AddRange(BuildTimeGridTasks(eventName, notification, booking));

            await Task.WhenAll(tasks);

            _logger.LogDebug(
                "SignalR broadcast: Event={Event}, BookingId={BookingId}, TimeGrid={TimeGrid}",
                eventName, notification.BookingId, includeTimeGrid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SignalR broadcast failed: Event={Event}, BookingId={BookingId}",
                eventName, notification.BookingId);
        }
    }

    /// <inheritdoc/>
    public async Task BroadcastPaymentEventAsync(
        string eventName,
        PaymentNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false)
    {
        try
        {
            var tasks = new List<Task>
            {
                // Group theo cá nhân: user_{CustomerId}
                _hubContext.Clients
                    .Group($"user_{booking.CustomerId}")
                    .SendAsync(eventName, notification),

                // Group theo chi nhánh: branch_{BranchId}
                _hubContext.Clients
                    .Group($"branch_{booking.BranchId}")
                    .SendAsync(eventName, notification),

                // Group theo quyền: role_OWNER
                _hubContext.Clients
                    .Group($"role_{Models.Enums.UserRole.OWNER}")
                    .SendAsync(eventName, notification),
            };

            if (includeTimeGrid)
                tasks.AddRange(BuildTimeGridTasks(eventName, notification, booking));

            await Task.WhenAll(tasks);

            _logger.LogDebug(
                "SignalR broadcast: Event={Event}, BookingId={BookingId}, TimeGrid={TimeGrid}",
                eventName, notification.BookingId, includeTimeGrid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SignalR broadcast failed: Event={Event}, BookingId={BookingId}",
                eventName, notification.BookingId);
        }
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Tạo danh sách các task gửi sự kiện cho mỗi tổ hợp duy nhất (branchId, courtTypeId, date)
    /// tìm thấy trong danh sách sân của booking. Yêu cầu BookingCourts và Court phải được nạp trước.
    /// </summary>
    private IEnumerable<Task> BuildTimeGridTasks(
        string eventName,
        object payload,
        Booking booking)
    {
        if (booking.BookingCourts == null) yield break;

        // Loại bỏ trùng lặp: chỉ gửi 1 lần cho mỗi cặp (branchId, courtTypeId, date) duy nhất
        var keys = booking.BookingCourts
            .Where(bc => bc.Court != null)
            .Select(bc => (
                BranchId: booking.BranchId,
                CourtTypeId: bc.Court.CourtTypeId,
                Date: bc.Date
            ))
            .Distinct();

        foreach (var key in keys)
        {
            var group = $"timegrid_{key.BranchId}_{key.CourtTypeId}_{key.Date:yyyy-MM-dd}";
            yield return _hubContext.Clients.Group(group).SendAsync(eventName, payload);
        }
    }
}
