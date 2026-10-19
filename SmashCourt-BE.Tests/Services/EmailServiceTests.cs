using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class EmailServiceTests
{
    [Fact]
    public async Task SendOtpRegisterAsync_MissingSmtpConfigurationThrowsConfigurationError()
    {
        var service = new EmailService(TestConfigurationFactory.Create());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendOtpRegisterAsync("user@example.com", "User", "123456"));
    }

    [Fact]
    public async Task SendOtpForgotPasswordAsync_MissingSmtpConfigurationThrowsConfigurationError()
    {
        var service = new EmailService(TestConfigurationFactory.Create());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendOtpForgotPasswordAsync(
            "user@example.com", "User", "123456"));
    }

    [Fact]
    public async Task SendOtp2FAAsync_MissingSmtpConfigurationThrowsConfigurationError()
    {
        var service = new EmailService(TestConfigurationFactory.Create());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendOtp2FAAsync(
            "user@example.com", "User", "123456"));
    }
}
