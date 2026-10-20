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
}
