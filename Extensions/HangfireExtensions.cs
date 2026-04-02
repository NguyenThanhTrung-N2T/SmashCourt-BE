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
        services.AddScoped<IPromotionJob, PromotionJob>();

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

        var vnTimezone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

        //  Mỗi 30 phút — dọn OTP hết hạn
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "cleanup-expired-otp",
            job => job.CleanupExpiredOtpAsync(),
            "*/30 * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // 3:00 AM VN time — dọn user chưa verify
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "cleanup-unverified-users",
            job => job.CleanupUnverifiedUsersAsync(),
            "0 3 * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // 3:00 AM VN time — dọn refresh token hết hạn
        RecurringJob.AddOrUpdate<IAuthCleanupJob>(
            "cleanup-expired-refresh-tokens",
            job => job.CleanupExpiredRefreshTokensAsync(),
            "0 3 * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // 00:00 AM VN time — cập nhật trạng thái promotion
        RecurringJob.AddOrUpdate<IPromotionJob>(
            "update-promotion-status",
            job => job.UpdateStatusAsync(),
            "0 0 * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        return app;
    }
}