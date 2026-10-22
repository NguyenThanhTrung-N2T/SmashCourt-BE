using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// D4 mặt còn lại: khi khóa vắng mặt hoặc bật, app thật vẫn đăng ký đủ 9 job định kỳ vào storage Hangfire thật (PostgreSQL).
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
        "cancel-expired-pending", "process-expired-bookings", "cleanup-slot-locks", "detect-no-show", "cleanup-slot-interests"
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

    private static IReadOnlyList<string> RegisterAndListRecurringJobIds(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireServices(config);
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<JobStorage>(); // đặt JobStorage.Current như lúc app khởi động

        new ApplicationBuilder(provider).UseHangfireServices(config);

        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs().Select(j => j.Id).OrderBy(id => id).ToList();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void UseHangfireServices_WhenEnabledAbsentOrTrue_RegistersAllNineRecurringJobs(string? enabled)
    {
        var ids = RegisterAndListRecurringJobIds(Config(enabled));

        Assert.Equal(ExpectedJobIds.OrderBy(id => id), ids);
    }

    [Fact]
    public void UseHangfireServices_WhenRunTwice_StaysAtNineJobsBecauseItUpsertsById()
    {
        // Khởi động lại app nhiều lần không nhân đôi job
        var config = Config(null);
        RegisterAndListRecurringJobIds(config);

        var ids = RegisterAndListRecurringJobIds(config);

        Assert.Equal(9, ids.Count);
    }
}
