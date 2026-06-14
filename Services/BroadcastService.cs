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
                // Personal: customer who made the booking
                _hubContext.Clients
                    .Group($"user_{booking.CustomerId}")
                    .SendAsync(eventName, notification),

                // Ops: Staff & Managers of the branch
                _hubContext.Clients
                    .Group($"branch_{booking.BranchId}")
                    .SendAsync(eventName, notification),

                // Role-wide: OWNER sees everything
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
                _hubContext.Clients
                    .Group($"user_{booking.CustomerId}")
                    .SendAsync(eventName, notification),

                _hubContext.Clients
                    .Group($"branch_{booking.BranchId}")
                    .SendAsync(eventName, notification),

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
    /// Builds a SendAsync task for each unique (branchId, courtTypeId, date) combination
    /// found in the booking's courts. Requires BookingCourts → Court to be loaded.
    /// </summary>
    private IEnumerable<Task> BuildTimeGridTasks(
        string eventName,
        object payload,
        Booking booking)
    {
        if (booking.BookingCourts == null) yield break;

        // De-duplicate: one push per unique (branchId, courtTypeId, date)
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
