namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Lịch sử đặt sân của khách trước booking đang xét (chỉ số đếm, không chứa dữ liệu cá nhân).</summary>
public class TadssHistoryDto
{
    public required int CompletedCount { get; set; }
    public required int NoShowCount { get; set; }
    public required int CancelledCount { get; set; }
    public int? LateCancelledCount { get; set; }
    public int? PaymentExpiredCount { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
}
