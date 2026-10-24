namespace SmashCourt_BE.Common.Constants;

/// <summary>
/// Định nghĩa tên sự kiện SignalR dùng chung giữa Backend và Frontend
/// </summary>
public static class SignalREvents
{
    // Booking Events
    public const string BookingCreated = "BookingCreated";
    public const string BookingExpired = "BookingExpired";
    public const string BookingUpdated = "BookingUpdated";
    public const string BookingCheckedIn = "BookingCheckedIn";
    public const string BookingCheckedOut = "BookingCheckedOut";
    public const string BookingPendingPayment = "BookingPendingPayment";
    public const string BookingCompleted = "BookingCompleted";
    public const string BookingCancelled = "BookingCancelled";
    public const string BookingRefunded = "BookingRefunded";
    public const string BookingNoShow = "BookingNoShow";
    
    // Payment Events
    public const string PaymentSuccess = "PaymentSuccess";
    public const string PaymentFailed = "PaymentFailed";

    // Deposit approval Events (Phase 6, DEPOSIT_APPROVAL_API 6) — FE: smashcourt-fe/src/lib/signalr-events.ts
    public const string BookingPendingApproval = "BookingPendingApproval";
    public const string BookingApprovalResolved = "BookingApprovalResolved";
    public const string PendingApprovalQueueChanged = "PendingApprovalQueueChanged";

    // Giá trị change / reason của PendingApprovalQueueChanged
    public const string QueueChangeAdded = "ADDED";
    public const string QueueChangeRemoved = "REMOVED";
    public const string QueueReasonNew = "NEW";
}
