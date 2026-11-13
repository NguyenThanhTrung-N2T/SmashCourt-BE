namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>Kết quả của một lần gọi đánh giá, theo cách mạch ngắt đếm (P6-57).</summary>
public enum TadssAttemptOutcome
{
    /// <summary>TADSS trả quyết định dùng được.</summary>
    Success,

    /// <summary>Lỗi cho thấy TADSS không trả lời được: timeout, lỗi mạng, 5xx. Mạch đếm loại này.</summary>
    Failure,

    /// <summary>TADSS có trả lời nhưng không dùng được (4xx, body hỏng): không đếm, không làm mạch đóng.</summary>
    Neutral
}
