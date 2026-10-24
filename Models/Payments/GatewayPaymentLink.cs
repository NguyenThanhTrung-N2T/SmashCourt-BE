namespace SmashCourt_BE.Models.Payments;

/// <summary>
/// Link thanh toán một cổng vừa tạo; <see cref="TransactionRef"/> là mã cổng sẽ gửi lại trong kết quả giao dịch.
/// </summary>
public sealed record GatewayPaymentLink(string Url, string TransactionRef);
