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
                    config.GetConnectionString("DefaultConnection")),
                new PostgreSqlStorageOptions
                {
                    // Tăng timeout cho distributed lock (default: 10 seconds)
                    DistributedLockTimeout = TimeSpan.FromSeconds(30),
                    // Tự động xóa lock sau 30 phút nếu process crash
                    InvisibilityTimeout = TimeSpan.FromMinutes(30)
                }));

        services.AddHangfireServer();
        services.AddScoped<IAuthCleanupJob, AuthCleanupJob>();
        services.AddScoped<IPromotionJob, PromotionJob>();
        services.AddScoped<IBookingJob, BookingJob>();

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
                    new DevHangfireAuthorizationFilter()
                }
            });
        }

        var vnTimezone = SmashCourt_BE.Helpers.DateTimeHelper.VNTimezone;

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

        // Mỗi 1 phút — hủy PENDING hết hạn + xử lý booking hết giờ
        RecurringJob.AddOrUpdate<IBookingJob>(
            "cancel-expired-pending",
            job => job.CancelExpiredPendingBookingsAsync(),
            "* * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        RecurringJob.AddOrUpdate<IBookingJob>(
            "process-expired-bookings",
            job => job.ProcessExpiredActiveBookingsAsync(),
            "* * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // Mỗi 1 phút — xóa slot_locks hết hạn
        // Lưu ý: Hangfire chỉ hỗ trợ cron 5-field tiêu chuẩn (không có seconds field)
        // "*/30 * * * * *" (6-field) sẽ bị parse lỗi → phải dùng "* * * * *" (mỗi phút)
        RecurringJob.AddOrUpdate<IBookingJob>(
            "cleanup-slot-locks",
            job => job.CleanupExpiredSlotLocksAsync(),
            "* * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // Mỗi 5 phút — phát hiện NO_SHOW
        RecurringJob.AddOrUpdate<IBookingJob>(
            "detect-no-show",
            job => job.DetectNoShowBookingsAsync(),
            "*/5 * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        // Mỗi 1 giờ — xóa slot_interest hết hạn (interest hết hạn sau ngày đặt sân)
        RecurringJob.AddOrUpdate<IBookingJob>(
            "cleanup-slot-interests",
            job => job.CleanupExpiredSlotInterestsAsync(),
            "0 * * * *",
            new RecurringJobOptions { TimeZone = vnTimezone });

        return app;
    }
}

public class DevHangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // Cho phép toàn bộ requests trong môi trường Development (truy cập qua Docker localhost/ngrok)
        return true;
    }
}