namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>Kết quả một lần gửi outcome theo cách job phải xử lý (gói 6A-5, C3 — P6-26).</summary>
public enum TadssOutcomeDeliveryKind
{
    /// <summary>TADSS đã có outcome của quyết định (200, hoặc 409 OUTCOME_ALREADY_RECORDED): đánh dấu đã báo.</summary>
    Recorded,

    /// <summary>TADSS từ chối riêng đơn này (quyết định không có, nội dung sai): để chu kỳ sau, làm tiếp đơn khác.</summary>
    Rejected,

    /// <summary>Hệ thống chưa sẵn sàng (timeout, mạng, 5xx, 429, 401, phản hồi không dùng được…): dừng chu kỳ.</summary>
    SystemFailure
}
