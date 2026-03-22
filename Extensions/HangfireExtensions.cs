using Hangfire;
using Hangfire.PostgreSql;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Jobs.Interfaces;

namespace SmashCourt_BE.Extensions;

public static class HangfireExtensions
{
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Cấu hình Hangfire với PostgreSQL
        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(
                    config.GetConnectionString("DefaultConnection"))));

        services.AddHangfireServer();
        services.AddScoped<IAuthCleanupJob, AuthCleanupJob>();

        return services;
    }

    // Cấu hình dashboard và đăng ký các job định kỳ
    public static IApplicationBuilder UseHangfireServices(
        this IApplicationBuilder app,
        IConfiguration config)
    {
        var isDashboardEnabled = config.GetValue<bool>("Hangfire:Dashboard");

        if (isDashboardEnabled)
        {
            var dashboardPath = config["Hangfire:DashboardPath"] ?? "/hangfire";
            app.UseHangfireDashboard(dashboardPath, new DashboardOptions
            {
                Authorization = new[]
                {
                    new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter()
                }
            });
        }

        // Mỗi giờ — dọn OTP hết hạn thường xuyên
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "hourly-otp-cleanup",
            job => job.CleanupExpiredOtpAsync(),
            "0 * * * *");

        // 3:00 AM — dọn user chưa verify
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "daily-user-cleanup",
            job => job.CleanupUnverifiedUsersAsync(),
            "0 3 * * *");

        // 3:00 AM — dọn refresh token hết hạn
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "daily-token-cleanup",
            job => job.CleanupExpiredRefreshTokensAsync(),
            "0 3 * * *");

        return app;
    }
}