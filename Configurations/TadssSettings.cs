namespace SmashCourt_BE.Configurations;

/// <summary>
/// Cấu hình tích hợp TADSS — ánh xạ từ appsettings.json section "TadssConfig".
/// Đọc qua <c>IOptionsMonitor</c> để đổi cấu hình có hiệu lực ngay. <see cref="ApiKey"/> chỉ đặt ở User Secrets / biến môi trường.
/// </summary>
public class TadssSettings
{
    public const string SectionName = "TadssConfig";

    /// <summary>Tắt thì BE không gọi TADSS, mọi đơn online cọc 100%.</summary>
    public bool EnableTadssDecision { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:5296";
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Thời gian chờ tối đa cho mỗi lần gọi TADSS (ms).</summary>
    public int TimeoutMs { get; set; } = 800;
    /// <summary>Hạn thanh toán cọc sau khi Manager duyệt (phút).</summary>
    public int ApprovedPaymentWindowMinutes { get; set; } = 15;
    /// <summary>Mã IANA gửi cho TADSS làm <c>branchTimeZone</c>.</summary>
    public string BranchTimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    // ── Mạch ngắt của lệnh đánh giá cọc (P6-57). Mặc định tắt; bản deploy, perf, E-STAGING bật bằng biến môi trường ──

    /// <summary>Bật mạch ngắt: TADSS không trả lời được thì đơn online cọc 100 % ngay, không chờ hết timeout ở mỗi đơn.</summary>
    public bool CircuitEnabled { get; set; }
    /// <summary>Số lần gọi đánh giá lỗi liên tiếp (timeout, lỗi mạng, 5xx) thì mở mạch dù kiểm sức khỏe chưa báo lỗi.</summary>
    public int CircuitFailureThreshold { get; set; } = 3;
    /// <summary>Chu kỳ kiểm <c>/health/ready</c> của TADSS khi mạch không đóng (giây).</summary>
    public int CircuitProbeIntervalSeconds { get; set; } = 10;
    /// <summary>Thời gian chờ tối đa của một lần kiểm sức khỏe (ms).</summary>
    public int CircuitProbeTimeoutMs { get; set; } = 300;
    /// <summary>Thời gian mở đầu tiên trước khi cho một đơn đi thử (giây); mỗi lần đơn thử hỏng thì gấp đôi.</summary>
    public int CircuitBreakSeconds { get; set; } = 60;
    /// <summary>Trần của thời gian mở khi gấp đôi (giây).</summary>
    public int CircuitMaxBreakSeconds { get; set; } = 300;
}
