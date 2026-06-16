namespace SmashCourt_BE.DTOs.SignalR;

/// <summary>
/// Payload thông tin thanh toán gửi qua SignalR
/// </summary>
public class PaymentNotificationDto
{
    public Guid BookingId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
