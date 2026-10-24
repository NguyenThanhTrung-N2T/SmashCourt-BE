namespace SmashCourt_BE.DTOs.SignalR;

/// <summary>
/// Payload của sự kiện <c>BookingPendingApproval</c>: đơn online mới cần Quản lý duyệt mức cọc.
/// Gửi cho nhân viên chi nhánh và OWNER; không có số tiền, lý do cần duyệt hay trường nội bộ của TADSS.
/// </summary>
public class BookingPendingApprovalNotificationDto
{
    public Guid BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public List<string> CourtIds { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public DateTime? ApprovalExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
