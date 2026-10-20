using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;

namespace SmashCourt_BE.Extensions;

public static class TadssExtensions
{
    /// <summary>
    /// Đăng ký cấu hình TADSS (kiểm khi khởi động, đọc qua <c>IOptionsMonitor</c>) và <see cref="ITadssClient"/>.
    /// Cần <c>TimeProvider</c> và <c>IHttpContextAccessor</c> đã đăng ký.
    /// </summary>
    public static IServiceCollection AddTadssIntegration(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<TadssSettings>()
            .Bind(config.GetSection(TadssSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TadssSettings>, TadssSettingsValidator>();

        services.AddHttpClient<ITadssClient, TadssClient>(client =>
            {
                // Timeout từng lần gọi do TadssClient tự cắt theo TimeoutMs (không dùng HttpClient.Timeout, không dùng Polly)
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // Không theo redirect: POST bị đổi thành GET và header X-Api-Key sẽ bị gửi tiếp sang host khác. 3xx được coi là lỗi HTTP
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return services;
    }
}
