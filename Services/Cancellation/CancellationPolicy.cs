using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Cancellation;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Cancellation;

/// <summary>
/// Luật dùng chung của mọi đường hủy. Hàm thuần: nhận "bây giờ" làm tham số; giờ chơi là giờ VN quy đổi UTC.
/// </summary>
public static class CancellationPolicy
{
    // Đã tới giờ bắt đầu sớm nhất của đơn
    public static bool HasStarted(DateOnly bookingDate, TimeOnly earliestStart, DateTime nowUtc) =>
        nowUtc >= DateTimeHelper.ToUtcFromVietnam(bookingDate, earliestStart);

    // % hoàn theo mốc chính sách lớn nhất không vượt số giờ còn lại; đã qua giờ chơi ⇒ 0
    public static decimal RefundPercent(IEnumerable<CancelPolicy> policies, DateTime playStartUtc, DateTime nowUtc)
    {
        var hoursUntilStart = (playStartUtc - nowUtc).TotalHours;
        if (hoursUntilStart < 0)
            return 0;

        return policies
            .Where(p => p.HoursBefore <= hoursUntilStart)
            .OrderByDescending(p => p.HoursBefore)
            .Select(p => p.RefundPercent)
            .FirstOrDefault();
    }

    // Hoàn trên số đã thu (Payment SUCCESS trừ phần đã hoàn), mỗi Payment một phần; bỏ phần bằng 0
    public static RefundQuote QuoteRefund(
        IEnumerable<CancelPolicy> policies, DateTime playStartUtc, DateTime nowUtc, IEnumerable<Payment> payments)
    {
        var percent = RefundPercent(policies, playStartUtc, nowUtc);
        var shares = payments
            .Where(p => p.Status == PaymentTxStatus.SUCCESS)
            .Select(p => new RefundShare(p.Id, Math.Round((p.Amount - p.RefundedAmount) * percent / 100, 0)))
            .Where(s => s.Amount > 0)
            .ToList();

        return new RefundQuote(percent, shares.Sum(s => s.Amount), shares);
    }

    // Lý do hủy của đơn PENDING hết hạn thanh toán (spec 4.4.1): SYSTEM_CLEANUP chỉ khi BE dừng giữa hai transaction của đặt online,
    // tức hóa đơn chưa có mức cọc và chưa có Payment nào. Đơn đã được duyệt mà khách chưa bấm thanh toán vẫn là PAYMENT_EXPIRED
    public static BookingCancelReason PaymentExpiryReason(Invoice? invoice) =>
        invoice?.DepositSource == null && (invoice?.Payments.Count ?? 0) == 0
            ? BookingCancelReason.SYSTEM_CLEANUP
            : BookingCancelReason.PAYMENT_EXPIRED;

    // Chỉ trả lượt khuyến mãi của đơn đã được tính lượt: đã có tiền về, hoặc thu tại quầy (lượt tính lúc xác nhận)
    public static bool ShouldReleasePromotionUse(Invoice invoice) =>
        invoice.PaymentTiming == PaymentTiming.POSTPAID
        || invoice.Payments.Any(p => p.Status == PaymentTxStatus.SUCCESS);
}
