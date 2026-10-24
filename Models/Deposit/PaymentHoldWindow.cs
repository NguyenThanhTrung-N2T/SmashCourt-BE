namespace SmashCourt_BE.Models.Deposit;

/// <summary>
/// Hạn thanh toán (<c>invoices.expires_at</c>) và hạn giữ chỗ tuyệt đối (<c>invoices.hold_deadline</c>) của đơn online, đều là UTC.
/// </summary>
public readonly record struct PaymentHoldWindow(DateTime ExpiresAt, DateTime HoldDeadline);
