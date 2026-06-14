using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Centralized SignalR broadcast service.
/// Broadcasts to four group types:
///   user_{userId}                          — personal channel for the customer
///   role_{ROLE}                            — role-wide channel (e.g. role_OWNER)
///   branch_{branchId}                      — ops channel for Staff/Manager of a branch
///   timegrid_{branchId}_{courtTypeId}_{date} — public channel for customers viewing the timegrid
/// </summary>
public interface IBroadcastService
{
    /// <summary>
    /// Broadcasts a booking-related event.
    /// </summary>
    /// <param name="eventName">SignalR event name from <see cref="SmashCourt_BE.Common.Constants.SignalREvents"/>.</param>
    /// <param name="notification">Payload to send.</param>
    /// <param name="booking">The booking entity, used to derive target groups. Must have BookingCourts with Court navigations loaded.</param>
    /// <param name="includeTimeGrid">
    /// When true, also broadcasts to <c>timegrid_...</c> groups for every court/date in the booking.
    /// Set to false for events that do not affect slot availability (e.g. check-in).
    /// </param>
    Task BroadcastBookingEventAsync(
        string eventName,
        BookingNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false);

    /// <summary>
    /// Broadcasts a payment-related event.
    /// </summary>
    /// <param name="eventName">SignalR event name.</param>
    /// <param name="notification">Payload to send.</param>
    /// <param name="booking">The booking entity. Must have BookingCourts with Court navigations loaded.</param>
    /// <param name="includeTimeGrid">
    /// When true, also broadcasts to <c>timegrid_...</c> groups.
    /// Use only when the payment outcome releases or holds a slot (e.g. payment failure cancels booking).
    /// </param>
    Task BroadcastPaymentEventAsync(
        string eventName,
        PaymentNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false);
}
