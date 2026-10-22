namespace SmashCourt_BE.Configurations;

/// <summary>
/// Giới hạn tốc độ của BE — ánh xạ từ appsettings.json section "RateLimiting" (B1/D9, Phase 6). Mỗi policy là một cặp
/// (số lượt, độ dài cửa sổ) <b>cho từng người</b> (xem <c>RateLimitPartitionKey</c>). Mặc định đúng bằng giá trị trước đây cố định
/// trong Program.cs. Tên khóa cấu hình là tên thuộc tính (VD <c>RateLimiting:AiPublic:PermitLimit</c> cho policy <c>ai-public</c>).
/// </summary>
public class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Tên policy dùng trong <c>[EnableRateLimiting("...")]</c> của controller.</summary>
    public static readonly IReadOnlyList<string> PolicyNames = ["login", "booking", "sensitive", "ai-public", "ai-user", "ai-management"];

    /// <summary>Đăng nhập — rất chặt.</summary>
    public RateLimitPolicySettings Login { get; set; } = new(5, 60);
    /// <summary>Đặt sân / thanh toán.</summary>
    public RateLimitPolicySettings Booking { get; set; } = new(20, 60);
    /// <summary>Upload / email.</summary>
    public RateLimitPolicySettings Sensitive { get; set; } = new(10, 60);
    /// <summary>AI công khai (FAQ, chat công khai).</summary>
    public RateLimitPolicySettings AiPublic { get; set; } = new(100, 60);
    /// <summary>AI cho khách hàng (gợi ý cá nhân hóa).</summary>
    public RateLimitPolicySettings AiUser { get; set; } = new(20, 60);
    /// <summary>AI cho quản lý (phân tích, giá, khuyến mãi) — chặt hơn vì tốn tài nguyên.</summary>
    public RateLimitPolicySettings AiManagement { get; set; } = new(10, 60);

    /// <summary>Cấu hình của policy theo tên dùng trong attribute.</summary>
    public RateLimitPolicySettings ForPolicy(string policyName) => policyName switch
    {
        "login" => Login,
        "booking" => Booking,
        "sensitive" => Sensitive,
        "ai-public" => AiPublic,
        "ai-user" => AiUser,
        "ai-management" => AiManagement,
        _ => throw new ArgumentOutOfRangeException(nameof(policyName), policyName, "Policy rate limit không tồn tại.")
    };
}

/// <summary>Giới hạn của một policy: tối đa <see cref="PermitLimit"/> lượt trong mỗi cửa sổ <see cref="WindowSeconds"/> giây.</summary>
public class RateLimitPolicySettings
{
    public RateLimitPolicySettings()
    {
    }

    public RateLimitPolicySettings(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}
