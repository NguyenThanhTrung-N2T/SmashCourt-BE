using Microsoft.Extensions.FileProviders;

namespace SmashCourt_BE.Extensions;

/// <summary>
/// Cấu hình đổi lúc chạy, không khởi động lại BE: file <c>runtime-config/feature-flags.json</c> ở content root. Bản deploy đặt thư mục này
/// trong một Docker volume và đổi cờ <c>TadssConfig:EnableTadssDecision</c> bằng <c>deploy/tadss-flag.sh on|off</c>; BE đọc cờ qua
/// <c>IOptionsMonitor</c> nên đơn online tạo sau lúc đổi dùng giá trị mới, đơn đang xử lý không bị gián đoạn.
/// </summary>
public static class RuntimeConfigExtensions
{
    public const string DirectoryName = "runtime-config";
    public const string FileName = "feature-flags.json";

    /// <summary>
    /// Thêm file cờ (tùy chọn, tự nạp lại khi đổi) làm nguồn cấu hình <b>cuối cùng</b>, nên thắng <c>appsettings*.json</c> và biến môi trường.
    /// Không có thư mục <see cref="DirectoryName"/> (dev, host test, CI) thì không thêm nguồn nào.
    /// </summary>
    public static IConfigurationBuilder AddSmashCourtRuntimeOverrides(this IConfigurationBuilder configuration, string contentRoot)
    {
        var directory = Path.Combine(contentRoot, DirectoryName);
        if (!Directory.Exists(directory))
            return configuration;

        // Provider gốc ở chính thư mục cờ: theo dõi cả lần ghi đè lẫn lần đổi tên vào (script ghi file tạm rồi mv)
        return configuration.AddJsonFile(new PhysicalFileProvider(directory), FileName, optional: true, reloadOnChange: true);
    }
}
