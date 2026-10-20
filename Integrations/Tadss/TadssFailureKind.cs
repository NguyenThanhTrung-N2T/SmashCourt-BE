namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Loại lỗi khi gọi TADSS. Service coi mọi loại là "TADSS lỗi" (cọc 100%) nhưng log và retry khác nhau.
/// </summary>
public enum TadssFailureKind
{
    /// <summary>Quá <c>TimeoutMs</c> mà chưa có phản hồi đầy đủ.</summary>
    Timeout,

    /// <summary>Không kết nối được hoặc đứt kết nối giữa chừng.</summary>
    Network,

    /// <summary>TADSS trả mã HTTP không phải 2xx (kèm <c>code</c> RFC 7807 nếu có).</summary>
    Http,

    /// <summary>HTTP 2xx nhưng body không dùng được (JSON hỏng, trang HTML của ngrok, thiếu trường bắt buộc, <c>status</c> lạ). Không thử lại.</summary>
    InvalidResponse
}
