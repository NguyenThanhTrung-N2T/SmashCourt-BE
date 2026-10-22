namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — cặp hạn giữ chỗ của hóa đơn online (spec Phase 6, 7.5). Cả hai mốc Kind = Utc.
    /// </summary>
    /// <param name="ExpiresAt">Hạn thanh toán hiện tại (<c>invoices.expires_at</c>); quá hạn thì Job-01 hủy đơn</param>
    /// <param name="HoldDeadline">Hạn giữ chỗ tuyệt đối (<c>invoices.hold_deadline</c>); thanh toán lại không vượt mốc này</param>
    public readonly record struct PaymentHoldWindow(DateTime ExpiresAt, DateTime HoldDeadline);
}
