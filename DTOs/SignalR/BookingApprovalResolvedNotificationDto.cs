namespace SmashCourt_BE.DTOs.SignalR;

/// <summary>
/// Payload của sự kiện <c>BookingApprovalResolved</c>: Quản lý vừa duyệt hoặc từ chối đơn chờ duyệt.
/// Gửi cho khách, nhân viên chi nhánh và OWNER; không có số tiền, lý do, SĐT, email hay trường nội bộ của TADSS
/// (FE tải lại chi tiết qua REST khi nhận sự kiện).
/// </summary>
public class BookingApprovalResolvedNotificationDto
{
    public Guid BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    /// <summary><c>APPROVED</c> | <c>REJECTED</c></summary>
    public string ApprovalResult { get; set; } = string.Empty;
    /// <summary>Trạng thái mới của booking: <c>PENDING</c>, <c>CONFIRMED</c> (duyệt 0 %) hoặc <c>CANCELLED</c></summary>
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
