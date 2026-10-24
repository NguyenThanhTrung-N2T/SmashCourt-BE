namespace SmashCourt_BE.Models.Deposit;

/// <summary>
/// Số đếm lịch sử của khách gửi TADSS.
/// </summary>
/// <param name="LastCompletedAtUtc">Giờ kết thúc chơi của đơn hoàn tất gần nhất; null khi chưa có</param>
public sealed record CustomerHistorySummary(
    int CompletedCount,
    int NoShowCount,
    int CancelledCount,
    int LateCancelledCount,
    int PaymentExpiredCount,
    DateTime? LastCompletedAtUtc);
