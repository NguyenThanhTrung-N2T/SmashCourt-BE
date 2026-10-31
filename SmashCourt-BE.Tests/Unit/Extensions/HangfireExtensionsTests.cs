using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Extensions;

/// <summary>
/// Khóa <c>Hangfire:Enabled</c> (D4, 6A-2a): mặc định bật nên production không đổi; tắt thì không đăng ký Hangfire (storage, server,
/// dashboard) và không đăng ký job định kỳ, để host test HTTP không chạy job thật trên DB test. Việc đăng ký đủ các job khi bật được
/// kiểm trên storage thật ở <c>Integration/Http/HangfireRegistrationTests</c>.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
public class HangfireExtensionsTests
{
    private static IConfiguration Config(string? enabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=never_connected;Username=x;Password=x"
        };
        if (enabled != null)
            values["Hangfire:Enabled"] = enabled;
        return TestConfigurationFactory.Create(values);
    }

    private static ServiceCollection Register(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddHangfireServices(config);
        return services;
    }

    private static bool HasHangfireInfrastructure(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IBackgroundJobClient)) ||
        services.Any(d => d.ServiceType == typeof(IHostedService));

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("True")]
    public void AddHangfireServices_WhenEnabledIsAbsentOrTrue_RegistersJobClientAndServer(string? enabled)
    {
        var services = Register(Config(enabled));

        Assert.Contains(services, d => d.ServiceType == typeof(IBackgroundJobClient));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    public void AddHangfireServices_WhenDisabled_RegistersNoHangfireInfrastructure(string enabled)
    {
        var services = Register(Config(enabled));

        Assert.False(HasHangfireInfrastructure(services));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void AddHangfireServices_Always_RegistersTheJobClassesForDirectUse(string? enabled)
    {
        // Job vẫn là dịch vụ bình thường (test có thể resolve và gọi trực tiếp) dù không có Hangfire
        var services = Register(Config(enabled));

        Assert.Contains(services, d => d.ServiceType == typeof(IAuthCleanupJob));
        Assert.Contains(services, d => d.ServiceType == typeof(IPromotionJob));
        Assert.Contains(services, d => d.ServiceType == typeof(IBookingJob));
        Assert.Contains(services, d => d.ServiceType == typeof(IBookingApprovalJob));
        Assert.Contains(services, d => d.ServiceType == typeof(ITadssOutcomeJob));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("1x")]
    public void AddHangfireServices_WhenEnabledIsNotABoolean_FailsFastInsteadOfSilentlyDefaulting(string enabled)
    {
        Assert.ThrowsAny<Exception>(() => Register(Config(enabled)));
    }

    [Fact]
    public void UseHangfireServices_WhenDisabled_RegistersNoRecurringJobAndNeedsNoStorage()
    {
        // Không có JobStorage: nếu còn gọi RecurringJob.AddOrUpdate thì sẽ ném (JobStorage.Current chưa đặt)
        JobStorage.Current = null!;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var result = app.UseHangfireServices(Config("false"));

        Assert.Same(app, result);
    }

    [Fact]
    public void UseHangfireServices_WhenDisabledEvenIfDashboardRequested_SkipsDashboard()
    {
        var config = TestConfigurationFactory.Create(new Dictionary<string, string?>
        {
            ["Hangfire:Enabled"] = "false",
            ["Hangfire:Dashboard"] = "true"
        });
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        // Dashboard cần dịch vụ Hangfire; tắt thì không được đụng tới
        var result = app.UseHangfireServices(config);

        Assert.Same(app, result);
    }

    [Fact]
    public void UseHangfireServices_WhenEnabledWithoutStorage_AttemptsToRegisterJobs()
    {
        // Đối chứng với ca tắt: khi bật, hàm thật sự chạm tới Hangfire (không có storage nên ném)
        JobStorage.Current = null!;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        Assert.ThrowsAny<Exception>(() => app.UseHangfireServices(Config("true")));
    }
}
