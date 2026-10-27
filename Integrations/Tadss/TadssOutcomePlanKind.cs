namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>Việc job báo kết quả làm với một booking (gói 6A-5, P6-26).</summary>
public enum TadssOutcomePlanKind
{
    /// <summary>Gửi outcome kèm request đã dựng.</summary>
    Send,

    /// <summary>Chưa kết thúc (hoặc chưa tới giờ kết thúc chơi): để chu kỳ sau.</summary>
    NotDue,

    /// <summary>Không gửi theo spec (SYSTEM_CLEANUP): đánh dấu đã báo để không chọn lại.</summary>
    Skip,

    /// <summary>Dữ liệu lệch (VD nhãn khách đến mâu thuẫn <c>checked_in_at</c>): giữ lại, không gửi, không đánh dấu.</summary>
    Invalid
}
