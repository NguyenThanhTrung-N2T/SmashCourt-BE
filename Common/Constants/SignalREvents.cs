namespace SmashCourt_BE.Common.Constants;

/// <summary>
/// Định nghĩa tên sự kiện SignalR dùng chung giữa Backend và Frontend
/// </summary>
public static class SignalREvents
{
    // Booking Events
    public const string BookingCreated = "BookingCreated";
    public const string BookingCheckedIn = "BookingCheckedIn";
    public const string BookingCheckedOut = "BookingCheckedOut";
    public const string BookingCancelled = "BookingCancelled";
    public const string BookingCompleted = "BookingCompleted";
    
    // Payment Events
    public const string PaymentSuccess = "PaymentSuccess";
    public const string PaymentFailed = "PaymentFailed";
}
