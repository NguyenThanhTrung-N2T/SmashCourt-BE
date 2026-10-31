namespace SmashCourt_BE.Models.Cancellation;

/// <summary>
/// Tiền hoàn khi hủy: % theo chính sách và một phần cho mỗi Payment đã thu. Hoàn bằng tiền mặt tại chi nhánh đặt sân.
/// </summary>
public sealed record RefundQuote(decimal Percent, decimal Amount, IReadOnlyList<RefundShare> Shares);

/// <summary>
/// Phần hoàn của một Payment; tách theo Payment để <c>refunded_amount</c> không vượt số tiền của Payment đó.
/// </summary>
public readonly record struct RefundShare(Guid PaymentId, decimal Amount);
