using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Extensions;

/// <summary>
/// Khóa phân vùng của rate limit (B1/D9): mỗi người một bộ đếm riêng cho từng policy, thay vì một bộ đếm chung cho cả hệ thống.
/// Đã đăng nhập ⇒ <c>user:{id}</c>; chưa đăng nhập ⇒ <c>ip:{địa chỉ}</c>; không xác định được nguồn ⇒ <c>unknown</c> (một khóa chung,
/// cố ý chặt thay vì bỏ qua giới hạn).
/// </summary>
public static class RateLimitPartitionKey
{
    public const string Unknown = "unknown";

    public static string Resolve(HttpContext context)
    {
        // Chỉ tin danh tính đã qua xác thực: claim tự dựng của client không được chọn khóa
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (!string.IsNullOrWhiteSpace(id))
                return $"user:{id}";
        }

        var address = context.Connection.RemoteIpAddress;
        if (address == null)
            return Unknown;

        // Kestrel dual-stack báo cùng một máy khách dưới dạng IPv4 hoặc IPv4-mapped IPv6
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return $"ip:{address}";
    }
}

public static class RateLimitExtensions
{
    /// <summary>
    /// Đăng ký 6 policy rate limit đếm theo từng người, giới hạn đọc từ <c>RateLimiting</c> (kiểm khi khởi động, đọc qua
    /// <c>IOptionsMonitor</c> lúc tạo bộ đếm của từng người nên đổi cấu hình áp cho người mới). Thân phản hồi 429 giữ nguyên.
    /// </summary>
    public static IServiceCollection AddSmashCourtRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RateLimitSettings>()
            .Bind(config.GetSection(RateLimitSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitSettings>, RateLimitSettingsValidator>();

        services.AddRateLimiter(options =>
        {
            foreach (var policyName in RateLimitSettings.PolicyNames)
            {
                var name = policyName;
                options.AddPolicy(name, context =>
                {
                    var limits = context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitSettings>>().CurrentValue.ForPolicy(name);
                    return RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitionKey.Resolve(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0
                    });
                });
            }

            options.RejectionStatusCode = 429;

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";

                var response = System.Text.Json.JsonSerializer.Serialize(
                    ApiResponse.Fail(
                        "Bạn gửi quá nhiều request, vui lòng thử lại sau",
                        ErrorCodes.OtpLimitExceeded  // 429 — dùng code rate-limit, không phải BadRequest
                    ),
                    new System.Text.Json.JsonSerializerOptions
                    {
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    }
                );

                await context.HttpContext.Response.WriteAsync(response, token);
            };
        });

        return services;
    }
}
