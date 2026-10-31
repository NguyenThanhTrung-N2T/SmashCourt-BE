namespace SmashCourt_BE.Configurations;

/// <summary>
/// Kiểm cấu hình bắt buộc lúc khởi động (Epic 6C, gói 6C-1 K4): image không chứa appsettings.Development.json, cấu hình đến từ file mount hoặc biến môi trường,
/// nên thiếu khóa phải lộ ra ngay khi khởi động thay vì thành lỗi 500 lúc dùng. Thông báo chỉ nêu tên khóa, không bao giờ nêu giá trị.
/// </summary>
public static class RequiredConfiguration
{
    // Thiếu thì không phục vụ được request nào (DB, đăng nhập, OTP) ⇒ dừng khởi động
    public static readonly IReadOnlyList<string> BlockingKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "Jwt:Key",
        "Jwt:Issuer",
        "Jwt:Audience",
        "Otp:HmacSecret",
    ];

    // Khóa của tính năng phụ: thiếu thì riêng tính năng đó lỗi lúc dùng ⇒ chỉ cảnh báo (môi trường đo và test không cần khóa thật)
    public static readonly IReadOnlyList<string> OptionalKeys =
    [
        "Smtp:Username", "Smtp:Password",
        "VnPay:TmnCode", "VnPay:HashSecret", "VnPay:CallbackUrl",
        "Cloudinary:CloudName", "Cloudinary:ApiKey", "Cloudinary:ApiSecret",
        "Google:ClientId", "Google:ClientSecret",
    ];

    // Giá trị mẫu trong appsettings.json (bản mẫu) không phải cấu hình thật
    private static readonly string[] PlaceholderPrefixes = ["your-", "add-"];

    /// <summary>Ném <see cref="InvalidOperationException"/> liệt kê mọi khóa cốt lõi còn thiếu hoặc còn giá trị mẫu.</summary>
    public static void EnsureBlockingKeys(IConfiguration configuration)
    {
        var missing = Missing(configuration, BlockingKeys);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Thiếu cấu hình bắt buộc: {string.Join(", ", missing)}. Đặt trong appsettings.Development.json (chạy local) "
                + "hoặc biến môi trường dạng Section__Key (container, GitHub Environment secrets).");
        }
    }

    /// <summary>Các khóa của tính năng phụ còn thiếu hoặc còn giá trị mẫu (để ghi cảnh báo).</summary>
    public static IReadOnlyList<string> MissingOptionalKeys(IConfiguration configuration) => Missing(configuration, OptionalKeys);

    private static List<string> Missing(IConfiguration configuration, IEnumerable<string> keys) =>
        keys.Where(key => IsUnset(configuration[key])).ToList();

    private static bool IsUnset(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || PlaceholderPrefixes.Any(prefix => value.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
