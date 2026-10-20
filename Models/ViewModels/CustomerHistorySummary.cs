namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — số đếm lịch sử của khách theo bảng lý do hủy (spec Phase 6, 4.4.1).
    /// </summary>
    /// <param name="LastCompletedAtUtc">Giờ kết thúc chơi của đơn COMPLETED gần nhất (UTC); NULL khi chưa hoàn tất đơn nào.</param>
    public sealed record CustomerHistorySummary(
        int CompletedCount,
        int NoShowCount,
        int CancelledCount,
        int LateCancelledCount,
        int PaymentExpiredCount,
        DateTime? LastCompletedAtUtc);
}
