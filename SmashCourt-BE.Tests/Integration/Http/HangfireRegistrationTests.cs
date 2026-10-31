using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// D4 mặt còn lại: khi khóa vắng mặt hoặc bật, app thật vẫn đăng ký đủ các job định kỳ vào storage Hangfire thật (PostgreSQL);
/// 6A-5 thêm job hết hạn duyệt (mỗi phút) và job báo kết quả về TADSS (mỗi 5 phút), giờ VN.
/// Dùng database riêng mỗi test để không đụng DB của bộ HTTP (nơi khẳng định Hangfire không ghi gì).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HangfireRegistrationTests(PostgreSqlIntegrationFixture database) : IAsyncLifetime
{
    private static readonly string[] ExpectedJobIds =
    [
        "cleanup-expired-otp", "cleanup-unverified-users", "cleanup-expired-refresh-tokens", "update-promotion-status",
        "cancel-expired-pending", "process-expired-bookings", "cleanup-slot-locks", "detect-no-show", "cleanup-slot-interests",
        "expire-approvals", "report-tadss-outcomes"
    ];

    private readonly string _databaseName = $"{SmashCourtWebApplicationFactory.DatabasePrefix}hf_{Guid.NewGuid():N}";
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await database.CreateIsolatedDatabaseAsync(_databaseName);
        // Bản dump chỉ có cấu trúc (bảng hangfire.schema không có dòng phiên bản) nên bộ cài đặt của Hangfire.PostgreSql coi schema chưa
        // cài và chờ khóa phân tán tới hết thời gian chờ. Bỏ schema để Hangfire tự cài như trên một database mới, đúng đường chạy thật.
        await database.ExecuteSqlInIsolatedDatabaseAsync(_databaseName, "DROP SCHEMA IF EXISTS hangfire CASCADE");
    }

    public async Task DisposeAsync()
    {
        JobStorage.Current = null!;
        await database.DropIsolatedDatabaseAsync(_databaseName);
    }

    private IConfiguration Config(string? enabled)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = _connectionString };
        if (enabled != null)
            values["Hangfire:Enabled"] = enabled;
        return TestConfigurationFactory.Create(values);
    }

    private static IReadOnlyList<string> RegisterAndListRecurringJobIds(IConfiguration config) =>
        RegisterAndListRecurringJobs(config).Select(j => j.Id).OrderBy(id => id).ToList();

    private static List<RecurringJobDto> RegisterAndListRecurringJobs(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireServices(config);
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<JobStorage>(); // đặt JobStorage.Current như lúc app khởi động

        new ApplicationBuilder(provider).UseHangfireServices(config);

        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void UseHangfireServices_WhenEnabledAbsentOrTrue_RegistersAllRecurringJobs(string? enabled)
    {
        var ids = RegisterAndListRecurringJobIds(Config(enabled));

        Assert.Equal(ExpectedJobIds.OrderBy(id => id), ids);
    }

    [Fact]
    public void UseHangfireServices_WhenRunTwice_KeepsOneEntryPerJobBecauseItUpsertsById()
    {
        // Khởi động lại app nhiều lần không nhân đôi job
        var config = Config(null);
        RegisterAndListRecurringJobIds(config);

        var ids = RegisterAndListRecurringJobIds(config);

        Assert.Equal(ExpectedJobIds.Length, ids.Count);
    }

    [Fact]
    public void UseHangfireServices_ApprovalExpiryJob_RunsEveryMinuteInVietnamTime()
    {
        var job = Assert.Single(RegisterAndListRecurringJobs(Config(null)), j => j.Id == "expire-approvals");

        Assert.Equal("* * * * *", job.Cron);
        Assert.Equal(SmashCourt_BE.Helpers.DateTimeHelper.VNTimezone.Id, job.TimeZoneId);
        Assert.Equal(typeof(SmashCourt_BE.Jobs.Interfaces.IBookingApprovalJob), job.Job.Type);
        Assert.Equal(nameof(SmashCourt_BE.Jobs.Interfaces.IBookingApprovalJob.ExpireApprovalsAsync), job.Job.Method.Name);
    }

    [Fact]
    public void UseHangfireServices_OutcomeJob_RunsEveryFiveMinutesInVietnamTime()
    {
        var job = Assert.Single(RegisterAndListRecurringJobs(Config(null)), j => j.Id == "report-tadss-outcomes");

        Assert.Equal("*/5 * * * *", job.Cron);
        Assert.Equal(SmashCourt_BE.Helpers.DateTimeHelper.VNTimezone.Id, job.TimeZoneId);
        Assert.Equal(typeof(SmashCourt_BE.Jobs.Interfaces.ITadssOutcomeJob), job.Job.Type);
        Assert.Equal(nameof(SmashCourt_BE.Jobs.Interfaces.ITadssOutcomeJob.ReportOutcomesAsync), job.Job.Method.Name);
    }
}
