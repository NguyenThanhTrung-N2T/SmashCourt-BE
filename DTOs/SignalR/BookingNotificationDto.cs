namespace SmashCourt_BE.DTOs.SignalR;

/// <summary>
/// Payload thông tin booking gửi qua SignalR
/// </summary>
public class BookingNotificationDto
{
    public Guid BookingId { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
