namespace SmashCourt_BE.DTOs.SignalR;

/// <summary>
/// Payload của sự kiện <c>PendingApprovalQueueChanged</c>: một đơn vào hoặc rời hàng chờ duyệt của chi nhánh.
/// FE tải lại hàng chờ khi nhận; <see cref="Change"/> ∈ ADDED | REMOVED, <see cref="Reason"/> ∈ NEW | APPROVED | REJECTED | EXPIRED | CANCELLED.
/// </summary>
public class PendingApprovalQueueChangedNotificationDto
{
    public Guid BranchId { get; set; }
    public Guid BookingId { get; set; }
    public string Change { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
