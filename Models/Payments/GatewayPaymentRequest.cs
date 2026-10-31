namespace SmashCourt_BE.Models.Payments;

/// <summary>
/// Yêu cầu tạo link thanh toán cho một cổng: số tiền VND, nội dung không dấu và hai mốc UTC (lúc tạo, hạn link). Mỗi cổng tự đổi mốc
/// sang định dạng của mình; hạn link do nghiệp vụ tính (PaymentHoldPolicy.TryGetPaymentLinkExpiry, P6-27).
/// </summary>
public sealed record GatewayPaymentRequest(
    string Reference, decimal Amount, string OrderInfo, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
