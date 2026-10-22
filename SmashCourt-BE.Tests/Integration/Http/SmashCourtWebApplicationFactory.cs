using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Chạy đúng <c>Program.cs</c> của BE trong tiến trình test (môi trường <c>Testing</c>) trên database riêng của bộ HTTP.
/// <para>
/// Cấu hình đọc sớm (chuỗi kết nối, JWT, VNPay: <c>Program.cs</c> đọc ngay khi khởi động, trước khi
/// <c>WebApplicationFactory</c> áp <c>ConfigureAppConfiguration</c>) được đặt bằng biến môi trường trước khi dựng host (B2).
/// Toàn bộ giá trị là giả: không khóa thật, không gọi dịch vụ ngoài. Hangfire tắt (D4) để job thật không chạy trên DB test.
/// </para>
/// </summary>
public sealed class SmashCourtWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string DatabasePrefix = "smashcourt_http_";
    public const string JwtKey = "http-test-jwt-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz";
    public const string JwtIssuer = "smashcourt-http-test";
    public const string JwtAudience = "smashcourt-http-test-audience";
    public const string TadssApiKey = "http-test-tadss-api-key";
    public const int DefaultRateLimit = 100_000;
    public const int TadssTimeoutMs = 400;

    private readonly Dictionary<string, string?> _previousEnvironment = new();

    public SmashCourtWebApplicationFactory(string connectionString, string tadssBaseUrl, IReadOnlyDictionary<string, string>? extraSettings = null)
    {
        AssertSafeConnectionString(connectionString);

        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__DefaultConnection"] = connectionString,
            ["Jwt__Key"] = JwtKey,
            ["Jwt__Issuer"] = JwtIssuer,
            ["Jwt__Audience"] = JwtAudience,
            ["Jwt__AccessTokenExpirationMinutes"] = "60",
            ["Otp__HmacSecret"] = "http-test-otp-hmac-secret",
            ["Smtp__Host"] = "127.0.0.1",
            ["Smtp__Port"] = "2525",
            ["Smtp__Username"] = "test@example.invalid",
            ["Smtp__Password"] = "not-a-real-password",
            ["Smtp__From"] = "test@example.invalid",
            ["VnPay__TmnCode"] = "HTTPTEST",
            ["VnPay__HashSecret"] = "HTTPTESTHASHSECRETHTTPTESTHASHSECRET",
            ["VnPay__CallbackUrl"] = "http://localhost/api/payments/vnpay-return",
            ["Google__ClientId"] = "http-test.apps.googleusercontent.com",
            ["Google__ClientSecret"] = "not-a-real-secret",
            ["Google__RedirectUri"] = "http://localhost/auth/google",
            ["Cloudinary__CloudName"] = "http-test",
            ["Cloudinary__ApiKey"] = "0",
            ["Cloudinary__ApiSecret"] = "not-a-real-secret",
            ["AIService__BaseUrl"] = "http://127.0.0.1:9", // cổng discard: kết nối bị từ chối ngay, không ra ngoài
            ["FrontendBaseUrl"] = "http://localhost:3000",
            ["TadssConfig__BaseUrl"] = tadssBaseUrl,
            ["TadssConfig__ApiKey"] = TadssApiKey,
            ["TadssConfig__EnableTadssDecision"] = "false",
            ["TadssConfig__TimeoutMs"] = TadssTimeoutMs.ToString(), // B6: ca timeout không chờ lâu (100 ms quá sát: lần gọi đầu bị JIT/kết nối nguội cắt oan)
            ["Hangfire__Enabled"] = "false"
        };
        // Bộ test gửi rất nhiều request nên mặc định không bị giới hạn tốc độ; test về rate limit tự hạ giới hạn bằng SetSetting (B1/D9)
        foreach (var policy in new[] { "Login", "Booking", "Sensitive", "AiPublic", "AiUser", "AiManagement" })
            settings[$"RateLimiting__{policy}__PermitLimit"] = DefaultRateLimit.ToString();
        if (extraSettings != null)
            foreach (var (key, value) in extraSettings)
                settings[key] = value;

        foreach (var (key, value) in settings)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>Thư BE đã "gửi" (không có SMTP thật).</summary>
    public RecordingEmailService Emails { get; } = new(new ConfigurationBuilder().Build());

    /// <summary>Đổi cấu hình lúc chạy (có báo reload cho <c>IOptionsMonitor</c>).</summary>
    public MutableConfigurationSource Overrides { get; } = new();

    public void SetSetting(string key, string? value) => Overrides.Provider.Update(key, value);

    /// <summary>Chặn nối nhầm DB thật: chỉ chấp nhận máy chủ loopback và database có tiền tố của bộ HTTP.</summary>
    public static void AssertSafeConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = builder.Host?.Trim().ToLowerInvariant();
        if (host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException($"Host test HTTP chỉ được nối máy chủ cục bộ, nhận '{builder.Host}'.");
        if (builder.Database is null || !builder.Database.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Host test HTTP chỉ được dùng database có tiền tố '{DatabasePrefix}', nhận '{builder.Database}'.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.Add(Overrides));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<EmailService>();
            services.AddSingleton(Emails);
            services.AddSingleton<EmailService>(sp => sp.GetRequiredService<RecordingEmailService>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        foreach (var (key, previous) in _previousEnvironment)
            Environment.SetEnvironmentVariable(key, previous);
        _previousEnvironment.Clear();
    }
}
